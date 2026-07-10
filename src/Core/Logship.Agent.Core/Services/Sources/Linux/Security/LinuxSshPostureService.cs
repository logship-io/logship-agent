// <copyright file="LinuxSshPostureService.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Logship.Agent.Core.Services.Sources.Linux.Security
{
    /// <summary>
    /// Snapshots SSH daemon posture from <c>sshd_config</c> and included config files.
    /// </summary>
    /// <remarks>
    /// Configure with <c>Sources:Linux.SshPosture</c>. <c>configPath</c> points to the primary
    /// <c>sshd_config</c> file, and <c>interval</c> controls how often posture is re-read. The parser
    /// follows <c>Include</c> directives with glob support and emits the effective value for selected
    /// security-sensitive settings. The service emits one <c>Linux.Ssh.Posture</c> record plus
    /// <c>Linux.Ssh.ConfigFile</c> metadata records for files that contributed to the posture. It reads
    /// configuration only and does not collect private host keys or user authorized-key contents.
    /// </remarks>
    internal sealed class LinuxSshPostureService : BaseIntervalInputService<LinuxSshPostureConfiguration>
    {
        private static readonly string[] SecuritySettings =
        [
            "Port",
            "ListenAddress",
            "PermitRootLogin",
            "PasswordAuthentication",
            "PermitEmptyPasswords",
            "PubkeyAuthentication",
            "KbdInteractiveAuthentication",
            "ChallengeResponseAuthentication",
            "AllowUsers",
            "AllowGroups",
            "DenyUsers",
            "DenyGroups",
            "X11Forwarding",
            "AllowTcpForwarding",
            "GatewayPorts",
            "PermitTunnel",
            "AuthenticationMethods",
            "AuthorizedKeysFile",
        ];

        public LinuxSshPostureService(IOptions<SourcesConfiguration> config, IEventBuffer buffer, ILogger<LinuxSshPostureService> logger)
            : base(config.Value.LinuxSshPosture, buffer, nameof(LinuxSshPostureService), logger)
        {
            if (this.Enabled && false == OperatingSystem.IsLinux())
            {
                ServiceLog.SkipPlatformServiceExecution(Logger, nameof(LinuxSshPostureService), Environment.OSVersion);
                this.Enabled = false;
            }
        }

        protected override bool ExitOnException => false;

        protected override Task ExecuteSingleAsync(CancellationToken token)
        {
            var result = LinuxSshConfigParser.Parse(this.Config.ConfigPath);

            var posture = CreateRecord("Linux.Ssh.Posture");
            posture.Data["ConfigPath"] = this.Config.ConfigPath;
            posture.Data["FilesRead"] = string.Join(';', result.FilesRead);
            posture.Data["IncludePatterns"] = string.Join(';', result.IncludePatterns);

            foreach (var key in SecuritySettings)
            {
                posture.Data[key] = result.Settings.TryGetValue(key, out var value) ? value : string.Empty;
            }

            this.Buffer.Add(posture);

            foreach (var file in result.FilesRead)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                var record = CreateRecord("Linux.Ssh.ConfigFile");
                LinuxFileMetadata.AddFileInfo(record.Data, new FileInfo(file));
                this.Buffer.Add(record);
            }

            return Task.CompletedTask;
        }
    }
}
