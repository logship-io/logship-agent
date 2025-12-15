namespace Logship.Agent.Core.Services.Sources.Linux.Syslog
{
    using Logship.Agent.Core.Internals.Utils;
    using System;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;

    internal sealed class SyslogOctetCountingParser
    {
        public static async Task<SyslogMessage?> TryParseAsync(StreamReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);

            if (false == char.IsAsciiDigit((char)reader.Peek()))
            {
                return null;
            }

            // This means the octet should be a number, lets read the whole thing
            var builder = new StringBuilder(5);
            while (char.IsAsciiDigit((char)reader.Peek()))
            {
                builder.Append((char)reader.Read());
                if (builder.Length >6) // safety
                {
                    return null;
                }
            }

            if (false == int.TryParse(builder.ToString(), out var length))
            {
                return null;
            }

            // The RFC specifies the octet count covers the syslog message bytes. We've already consumed the length digits from the stream
            var remainingOctets = length - builder.Length;
            if (remainingOctets <=0) return null;

            var buffer = new byte[remainingOctets];
            var ok = await reader.ReadExectlyAsync(buffer).ConfigureAwait(false);
            if (false == ok)
            {
                return null;
            }

            // Decode as UTF8 and pass to RFC5424 reader
            try
            {
                using var ms = new MemoryStream(buffer, writable: false);
                using var innerReader = new StreamReader(ms, Encoding.UTF8);
                var parsed = await SyslogRfc5424Reader.TryParseAsync(innerReader).ConfigureAwait(false);
                return parsed;
            }
            catch
            {
                return null;
            }
        }
    }
}
