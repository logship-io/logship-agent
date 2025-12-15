namespace Logship.Agent.Core.Services.Sources.Linux.Syslog
{
    using Logship.Agent.Core.Internals.Utils;
    using System;
    using System.Globalization;
    using System.IO;
    using System.Threading.Tasks;

    internal static class SyslogRfc3164Reader
    {
        public static async Task<SyslogMessage?> TryParseAsync(StreamReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line == null) return null;
            return TryParseFromSpan(line.AsSpan());
        }

        internal static SyslogMessage? TryParseFromSpan(ReadOnlySpan<char> span)
        {
            // Message looks like this.
            // <34>Oct 3 10:15:32 mymachine su[12345]: 'su root' failed for user on /dev/pts/0

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

            span = span.Slice(closingIndex + 1);

            // Next, read the timestamp in the format "Mmm dd hh:mm:ss"
            var dateLength = 0;
            var seenSpaces = 0;
            while (seenSpaces < 3 && dateLength < span.Length)
            {
                if (span[dateLength] == ' ')
                {
                    seenSpaces++;
                }

                dateLength++;
            }

            var dateString = span.Slice(0, dateLength);
            if (false == TryParseDate(dateString, out var timestamp))
            {
                return null;
            }

            span = span.Slice(dateLength);

            // Next, read the hostname
            if (false == span.TryNexToken(' ', out var hostname))
            {
                return null;
            }

            span = span.Slice(hostname.Length + 1);

            // Next, read the process
            if (false == span.TryNexToken('[', out var processToken))
            {
                return null;
            }

            span = span.Slice(processToken.Length + 1);

            // Next, read the process id
            if (false == span.TryNexToken(']', out var procIdToken))
            {
                return null;
            }

            span = span.Slice(procIdToken.Length + 3); // Skip : and the following space

            return new SyslogMessage(
                timestamp: timestamp,
                priority: priority,
                version: 0,
                hostname: hostname.ToString(),
                appName: processToken.ToString(),
                process: processToken.ToString(),
                procId: procIdToken.ToString(),
                messageId: string.Empty,
                structuredData: new Dictionary<string, string>(),
                message: span.ToString()
            );
        }

        private static readonly string[] Months = new[]
        {
            "Jan", "Feb", "Mar", "Apr", "May", "Jun",
            "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"
        };

        private static bool TryParseDate(ReadOnlySpan<char> span, out DateTimeOffset timestamp)
        {
            // Date format is "Mmm dd hh:mm:ss"
            var parts = span.ToString().Split(' ');
            if (parts.Length < 3)
            {
                timestamp = default;
                return false;
            }

            var monthStr = parts[0];
            var dayStr = parts[1];
            var timeStr = parts[2];

            int month = -1;
            for (var i = 0; i < Months.Length; i++)
            {
                if (string.Equals(Months[i], monthStr, StringComparison.OrdinalIgnoreCase))
                {
                    month = i;
                    break;
                }
            }

            if (month == -1)
            {
                timestamp = default;
                return false;
            }

            if (false == int.TryParse(dayStr, out var day))
            {
                timestamp = default;
                return false;
            }

            if (false == TimeSpan.TryParse(timeStr, out var timeOfDay))
            {
                timestamp = default;
                return false;
            }

            var now = DateTimeOffset.UtcNow;
            timestamp = new DateTimeOffset(now.Year, month + 1, day, timeOfDay.Hours, timeOfDay.Minutes, timeOfDay.Seconds, now.Offset);
            return true;

            // return DateTimeOffset.TryParseExact(span.ToString(), "MMM d HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out timestamp);
        }
    }
}
