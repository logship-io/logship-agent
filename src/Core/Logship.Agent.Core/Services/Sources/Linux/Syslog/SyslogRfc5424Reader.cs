namespace Logship.Agent.Core.Services.Sources.Linux.Syslog
{
    using Logship.Agent.Core.Internals.Utils;
    using System;
    using System.Globalization;
    using System.IO;
    using System.Threading.Tasks;

    internal static class SyslogRfc5424Reader
    {
        public static async Task<SyslogMessage?> TryParseAsync(StreamReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);

            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line == null) return null;

            var result = TryParseFromSpan(line.AsSpan());
            if (result != null) return result;

            // fallback to RFC3164
            return SyslogRfc3164Reader.TryParseFromSpan(line.AsSpan());
        }

        internal static SyslogMessage? TryParseFromSpan(ReadOnlySpan<char> span)
        {
            var originalSpan = span;

            // Quick validation
            if (span.IsEmpty || span[0] != '<')
            {
                return null;
            }

            // The message format looks like this:
            // <34>1 2025-01-03T14:07:15.003Z mymachine.example.com su 12345 ID47 - 'su root' failed for user on /dev/pts/0

            // First, read the message priority in the format <num>
            // Find the index of the closing sign >
            var closingIndex = span.IndexOf('>');
            if (closingIndex == -1)
            {
                return null;
            }

            // Grab the priority from in between
            var priorityString = span.Slice(1, closingIndex - 1);
            if (priorityString.Length == 0)
            {
                return null;
            }

            if (false == int.TryParse(priorityString, out var priority))
            {
                return null;
            }

            // Move the span past the priority
            span = span.Slice(closingIndex);

            // The version must be 1
            if (span[0] != '1')
            {
                return null;
            }

            span = span.Slice(2);

            // Now parse the date, so grab the date token as a string
            if (false == span.TryNexToken(' ', out var dateToken))
            {
                return null;
            }

            if (span.Length < 5)
            {
                return SyslogRfc3164Reader.TryParseFromSpan(originalSpan);
            }

            if (false == DateTimeOffset.TryParse(dateToken, CultureInfo.InvariantCulture, out var timestamp))
            {
                return null;
            }

            // Move past the date token
            span = span.Slice(dateToken.Length + 1);

            // Now parse the hostname
            if (false == span.TryNexToken(' ', out var hostnameToken))
            {
                return null;
            }

            span = span.Slice(hostnameToken.Length + 1);
            // Now parse the app name

            if (false == span.TryNexToken(' ', out var appNameToken))
            {
                return null;
            }

            span = span.Slice(appNameToken.Length + 1);

            // Now parse the process
            if (false == span.TryNexToken(' ', out var processToken))
            {
                return null;
            }

            span = span.Slice(processToken.Length + 1);

            // Now parse the proc id
            if (false == span.TryNexToken(' ', out var procIdToken))
            {
                return null;
            }

            span = span.Slice(procIdToken.Length + 1);
            // Now parse the message id
            if (false == span.TryNexToken(' ', out var messageIdToken))
            {
                return null;
            }

            span = span.Slice(messageIdToken.Length + 1);

            var structuredData = new Dictionary<string, string>();

            // Now parse the structured data
            if (span[0] == '-')
            {
                // No structured data
                span = span.Slice(2);
            }

            while (span[0] == '[')
            {
                // find the closing ]
                var closingBracketIndex = span.IndexOf(']');
                if (closingBracketIndex == -1)
                {
                    return null;
                }

                // Grab the while structured data element
                var sdElement = span.Slice(0, closingBracketIndex - 1);

                var split = sdElement.Split(' ');

                // Move past the sd id
                if (false == split.MoveNext())
                {
                    break;
                }

                while (split.MoveNext())
                {
                    var kvp = split.Current;
                    var str = span.Slice(kvp.Start.Value, kvp.End.Value - kvp.Start.Value);

                    var equalIndex = str.IndexOf('=');
                    if (equalIndex == -1)
                    {
                        continue;
                    }

                    var key = str.Slice(0, equalIndex).ToString();
                    var value = str.Slice(equalIndex + 1).ToString().Trim('"');

                    structuredData[key] = value;
                }

                span = span.Slice(closingBracketIndex);
            }

            // The rest is the message
            var message = span.Length > 0 ? span.ToString() : string.Empty;

            return new SyslogMessage(
                priority: priority,
                version: 1,
                timestamp: timestamp,
                hostname: hostnameToken.ToString(),
                appName: appNameToken.ToString(),
                process: processToken.ToString(),
                procId: procIdToken.ToString(),
                messageId: messageIdToken.ToString(),
                structuredData: structuredData,
                message: message);
        }
    }
}
