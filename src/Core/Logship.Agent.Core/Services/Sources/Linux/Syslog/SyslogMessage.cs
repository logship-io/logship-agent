using System;

namespace Logship.Agent.Core.Services.Sources.Linux.Syslog
{
    /// <summary>
    /// Immutable representation of an RFC 5424 syslog message (and common RFC3164 fields).
    /// Parsers in the same namespace should construct instances with the parsed values.
    /// </summary>
    internal sealed class SyslogMessage
    {
        public SyslogMessage(
            int priority,
            int version,
            DateTimeOffset timestamp,
            string hostname,
            string appName,
            string process,
            string procId,
            string messageId,
            Dictionary<string, string> structuredData,
            string message)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(priority);
            ArgumentOutOfRangeException.ThrowIfNegative(version);
            Timestamp = timestamp;
            Priority = priority;
            Version = version;
            Hostname = hostname ?? throw new ArgumentNullException(nameof(hostname));
            AppName = appName ?? throw new ArgumentNullException(nameof(appName));
            Process = process ?? throw new ArgumentNullException(nameof(process));
            ProcId = procId ?? throw new ArgumentNullException(nameof(procId));
            MessageId = messageId ?? throw new ArgumentNullException(nameof(messageId));
            StructuredData = structuredData ?? throw new ArgumentNullException(nameof(structuredData));
            Message = message ?? string.Empty;
        }

        // Core fields
        public DateTimeOffset Timestamp { get; }
        public int Priority { get; }
        public int Version { get; }
        public string Hostname { get; }
        public string AppName { get; }
        public string Process { get; }
        public string ProcId { get; }
        public string MessageId { get; }
        public Dictionary<string, string> StructuredData { get; }
        public string Message { get; }

        // Derived helpers
        public int Facility => Priority / 8;
        public int Severity => Priority % 8;

        public override string ToString()
            => $"{Timestamp:O} {Hostname} {AppName}[{ProcId}]: {Message}";

        // Small helper to create a lightweight message when fewer fields are available
        public static SyslogMessage CreateMinimal(int priority, DateTimeOffset timestamp, string process, string message)
            => new SyslogMessage(
                priority: priority,
                version: 1,
                timestamp: timestamp,
                hostname: "-",
                appName: process,
                process: process,
                procId: "-",
                messageId: "-",
                structuredData: new Dictionary<string, string>(),
                message: message);
    }
}
