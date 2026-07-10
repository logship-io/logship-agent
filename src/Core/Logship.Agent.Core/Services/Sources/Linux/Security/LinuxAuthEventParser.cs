// <copyright file="LinuxAuthEventParser.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using System.Text.RegularExpressions;

namespace Logship.Agent.Core.Services.Sources.Linux.Security
{
    internal static partial class LinuxAuthEventParser
    {
        public static Dictionary<string, object>? TryParse(string line, string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            var data = new Dictionary<string, object>
            {
                ["SourcePath"] = sourcePath,
                ["RawMessage"] = line,
            };

            var prefix = SyslogPrefixRegex().Match(line);
            var message = line;
            if (prefix.Success)
            {
                data["SyslogTimestamp"] = prefix.Groups["timestamp"].Value;
                data["Host"] = prefix.Groups["host"].Value;
                data["Process"] = prefix.Groups["process"].Value;
                if (prefix.Groups["pid"].Success)
                {
                    data["Pid"] = prefix.Groups["pid"].Value;
                }

                message = prefix.Groups["message"].Value;
            }

            if (TryParseSsh(message, data)
                || TryParseSudo(message, data)
                || TryParseSu(message, data)
                || TryParsePam(message, data))
            {
                return data;
            }

            return null;
        }

        private static bool TryParseSsh(string message, Dictionary<string, object> data)
        {
            var accepted = SshAcceptedRegex().Match(message);
            if (accepted.Success)
            {
                data["EventType"] = "ssh_login";
                data["Result"] = "success";
                data["AuthMethod"] = accepted.Groups["method"].Value;
                data["User"] = accepted.Groups["user"].Value;
                data["SourceAddress"] = accepted.Groups["address"].Value;
                data["SourcePort"] = accepted.Groups["port"].Value;
                data["Service"] = "sshd";
                return true;
            }

            var failed = SshFailedRegex().Match(message);
            if (failed.Success)
            {
                data["EventType"] = "ssh_login";
                data["Result"] = "failure";
                data["AuthMethod"] = failed.Groups["method"].Value;
                data["User"] = failed.Groups["user"].Value;
                data["InvalidUser"] = failed.Groups["invalid"].Success;
                data["SourceAddress"] = failed.Groups["address"].Value;
                data["SourcePort"] = failed.Groups["port"].Value;
                data["Service"] = "sshd";
                return true;
            }

            return false;
        }

        private static bool TryParseSudo(string message, Dictionary<string, object> data)
        {
            var command = SudoCommandRegex().Match(message);
            if (command.Success)
            {
                data["EventType"] = "sudo_command";
                data["Result"] = "success";
                data["User"] = command.Groups["user"].Value;
                data["TargetUser"] = command.Groups["target"].Value;
                data["Command"] = command.Groups["command"].Value;
                data["WorkingDirectory"] = command.Groups["pwd"].Value;
                data["Tty"] = command.Groups["tty"].Value;
                data["Service"] = "sudo";
                return true;
            }

            var failure = SudoFailureRegex().Match(message);
            if (failure.Success)
            {
                data["EventType"] = "sudo_auth";
                data["Result"] = "failure";
                data["User"] = ExtractField(message, "user");
                data["Service"] = "sudo";
                return true;
            }

            return false;
        }

        private static bool TryParseSu(string message, Dictionary<string, object> data)
        {
            var suSession = SuSessionRegex().Match(message);
            if (suSession.Success)
            {
                data["EventType"] = "su_session";
                data["Result"] = suSession.Groups["action"].Value.Equals("opened", StringComparison.OrdinalIgnoreCase) ? "success" : "closed";
                data["TargetUser"] = suSession.Groups["target"].Value;
                data["User"] = suSession.Groups["user"].Value;
                data["Service"] = "su";
                return true;
            }

            return false;
        }

        private static bool TryParsePam(string message, Dictionary<string, object> data)
        {
            var pamFailure = PamFailureRegex().Match(message);
            if (pamFailure.Success)
            {
                data["EventType"] = "pam_auth";
                data["Result"] = "failure";
                data["Service"] = pamFailure.Groups["service"].Value;
                data["User"] = ExtractField(message, "user");
                data["RemoteUser"] = ExtractField(message, "ruser");
                data["SourceAddress"] = ExtractField(message, "rhost");
                return true;
            }

            var pamSession = PamSessionRegex().Match(message);
            if (pamSession.Success)
            {
                data["EventType"] = "pam_session";
                data["Result"] = pamSession.Groups["action"].Value.Equals("opened", StringComparison.OrdinalIgnoreCase) ? "success" : "closed";
                data["Service"] = pamSession.Groups["service"].Value;
                data["TargetUser"] = pamSession.Groups["target"].Value;
                data["User"] = pamSession.Groups["user"].Value;
                return true;
            }

            return false;
        }

        private static string ExtractField(string message, string field)
        {
            var match = Regex.Match(message, $@"(?:^|\s){Regex.Escape(field)}=(?<value>\S*)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["value"].Value : string.Empty;
        }

        [GeneratedRegex(@"^(?<timestamp>\w{3}\s+\d{1,2}\s+\d{2}:\d{2}:\d{2})\s+(?<host>\S+)\s+(?<process>[^\[:]+)(?:\[(?<pid>\d+)\])?:\s+(?<message>.*)$")]
        private static partial Regex SyslogPrefixRegex();

        [GeneratedRegex(@"Accepted (?<method>\S+) for (?<user>\S+) from (?<address>\S+) port (?<port>\d+)", RegexOptions.IgnoreCase)]
        private static partial Regex SshAcceptedRegex();

        [GeneratedRegex(@"Failed (?<method>\S+) for (?:(?<invalid>invalid user)\s+)?(?<user>\S+) from (?<address>\S+) port (?<port>\d+)", RegexOptions.IgnoreCase)]
        private static partial Regex SshFailedRegex();

        [GeneratedRegex(@"^(?<user>\S+)\s+:\s+TTY=(?<tty>[^;]+)\s+;\s+PWD=(?<pwd>[^;]+)\s+;\s+USER=(?<target>[^;]+)\s+;\s+COMMAND=(?<command>.*)$", RegexOptions.IgnoreCase)]
        private static partial Regex SudoCommandRegex();

        [GeneratedRegex(@"pam_unix\(sudo:auth\): authentication failure", RegexOptions.IgnoreCase)]
        private static partial Regex SudoFailureRegex();

        [GeneratedRegex(@"pam_unix\(su:session\): session (?<action>opened|closed) for user (?<target>\S+).* by (?<user>\S+)", RegexOptions.IgnoreCase)]
        private static partial Regex SuSessionRegex();

        [GeneratedRegex(@"pam_unix\((?<service>[^:]+):auth\): authentication failure", RegexOptions.IgnoreCase)]
        private static partial Regex PamFailureRegex();

        [GeneratedRegex(@"pam_unix\((?<service>[^:]+):session\): session (?<action>opened|closed) for user (?<target>\S+).* by (?<user>\S+)", RegexOptions.IgnoreCase)]
        private static partial Regex PamSessionRegex();
    }
}
