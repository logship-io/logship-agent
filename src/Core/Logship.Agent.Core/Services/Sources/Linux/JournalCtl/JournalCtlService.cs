using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Logship.Agent.Core.Inputs.Linux.JournalCtl;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Logship.Agent.Core.Services.Sources.Linux.JournalCtl
{
    internal sealed class JournalCtlService : BaseConfiguredService<JournalCtlConfiguration>
    {
        private readonly IEventBuffer buffer;
        private int flags;
        private HashSet<string> fields;
        private static string[] DEFAULT_FIELDS =
        [
            "MESSAGE",
            "PRIORITY",
            "SYSLOG_IDENTIFIER",
            "SYSLOG_FACILITY",
            "SYSLOG_PID",
            "SYSLOG_TIMESTAMP",
            "SYSTEMD_UNIT",
            "SOURCE_REALTIME_TIMESTAMP",
        ];

        public JournalCtlService(IOptions<SourcesConfiguration> config, IEventBuffer buffer, ILogger<JournalCtlService> logger)
            : base(nameof(JournalCtlService), config.Value.JournalCtl, logger)
        {
            this.buffer = buffer;
            fields = new HashSet<string>(DEFAULT_FIELDS);
            this.flags = this.Config.Flags;
            foreach (var column in this.Config.IncludeFields)
            {
                this.fields.Add(column);
            }

            if (this.Enabled && false == OperatingSystem.IsLinux())
            {
                ServiceLog.SkipPlatformServiceExecution(Logger, nameof(JournalCtlService), Environment.OSVersion);
                this.Enabled = false;
            }
        }

        protected override Task ExecuteAsync(CancellationToken token)
        {
            RunSync(token);
            return Task.CompletedTask;
        }

        void RunSync(CancellationToken token)
        {
            using var journal = JournalHandle.Open(this.Logger, flags);
            int seekResult = Interop.sd_journal_seek_tail(journal.DangerousGetHandle());
            if (seekResult < 0)
            {
                Interop.Throw(seekResult, "Error during sd_journal_seek_tail");
            }

            // Go back and forth one entry.
            // After upgrading to Ubuntu 24 we never receive *any* entries after seeking tail
            // This fixes it, I don't know.
            _ = Interop.sd_journal_previous(journal.DangerousGetHandle());
            _ = Interop.sd_journal_next(journal.DangerousGetHandle());

            int result;
            int count = 0;
            do
            {
                result = Interop.sd_journal_next(journal.DangerousGetHandle());
                JournalCtlLog.NextResult(Logger, result);
                switch (result)
                {
                    case 1:
                        count++;
                        ReadEntry(journal, token);
                        break;
                    case 0:
                        if (count > 0)
                        {
                            JournalCtlLog.Blocking(Logger, count);
                            count = 0;
                        }

                        WaitForJournal(journal, token);
                        break;
                    default:
                        Interop.Throw(result, "Error during sd_journal_next");
                        break;
                }
            } while (false == token.IsCancellationRequested);
        }

        private static void WaitForJournal(JournalHandle handle, CancellationToken token)
        {
            int r;
            do
            {
                r = Interop.sd_journal_wait(handle.DangerousGetHandle(), 5_000_000);
                if (r < 0)
                {
                    Interop.Throw(r, "Failed to wait for new journal entry");
                }
            } while (false == token.IsCancellationRequested && r == 0);
        }

        async void ReadEntry(JournalHandle journal, CancellationToken cancellationToken)
        {
            var fields = new Dictionary<string, object>
            {
                { "machine", Environment.MachineName },
            };

            var extraData = new Dictionary<string, string>();
            int result;
            do
            {
                result = Interop.sd_journal_enumerate_available_data(journal.DangerousGetHandle(), out var data_ptr, out var data_size);
                if (result < 0)
                {
                    Interop.Throw(result, "Error while reading journal entry.");
                }
                else if (result == 0)
                {
                    break;
                }


                // Convert the field name to a string
                string field = Marshal.PtrToStringUTF8((nint)data_ptr, (int)data_size);
                var parts = field.Split(['='], 2);
                if (parts.Length != 2)
                {
                    continue;
                }

                string fieldName = parts[0];
                string fieldValue = parts[1];
                if (this.fields.Contains(fieldName))
                {
                    fields[fieldName] = fieldValue;
                }
                else
                {
                    extraData[fieldName] = fieldValue;
                }
            } while (false == cancellationToken.IsCancellationRequested);

            if (false == IncludeRecord(fields, extraData))
            {
                return;
            }

            using var stream = new MemoryStream(extraData.Count * 100);
            await JsonSerializer.SerializeAsync(stream, extraData, JournalCtlSourceGenerationContext.Default.DictionaryStringString, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            fields["ExtraData"] = Encoding.UTF8.GetString(stream.ToArray());
            buffer.Add(new Records.DataRecord("Linux.JournalD", DateTimeOffset.UtcNow, fields));
        }

        private bool IncludeRecord(Dictionary<string, object> fields, Dictionary<string, string> extraData)
        {
            if (this.Config.Filters.Count == 0)
            {
                return true;
            }

            foreach (JournalCtlFilterTypeConfiguration filter in this.Config.Filters)
            {
                if (MatchesFilterType(filter, fields, extraData))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesFilterType(JournalCtlFilterTypeConfiguration filter, Dictionary<string, object> fields, Dictionary<string, string> extraData)
        {
            if (filter.MatchAll != null && filter.MatchAll.Count > 0)
            {
                foreach (JournalCtlFilterConfiguration mustMatch in filter.MatchAll)
                {
                    if (false == MatchesFilter(mustMatch, fields, extraData))
                    {
                        return false;
                    }
                }
            }

            if (filter.MatchAny != null && filter.MatchAny.Count > 0)
            {
                foreach (JournalCtlFilterConfiguration matchAny in filter.MatchAny)
                {
                    if (MatchesFilter(matchAny, fields, extraData))
                    {
                        return true;
                    }
                }

                return false;
            }

            return true;
        }

        private static bool MatchesFilter(JournalCtlFilterConfiguration filter, Dictionary<string, object> fields, Dictionary<string, string> extraData)
        {
            if (false == string.IsNullOrEmpty(filter.HasField))
            {
                if (false == fields.ContainsKey(filter.HasField)
                    && false == extraData.ContainsKey(filter.HasField))
                {
                    return false;
                }
            }

            if (filter.FieldEquals != null
                && false == string.IsNullOrEmpty(filter.FieldEquals.Field))
            {
                bool fieldsContains = fields.ContainsKey(filter.FieldEquals.Field);
                bool extraDataContains = extraData.ContainsKey(filter.FieldEquals.Field);
                if (false == fieldsContains
                    && false == extraDataContains)
                {
                    return false;
                }
                else if (fieldsContains
                    && false == ((string)fields[filter.FieldEquals.Field]).Equals(filter.FieldEquals.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                else if (extraDataContains
                    && false == extraData[filter.FieldEquals.Field].Equals(filter.FieldEquals.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }


    }

    internal static partial class JournalCtlLog
    {
        [LoggerMessage(LogLevel.Trace, "JournalCtl blocking. Last flush was {Count} entries.")]
        public static partial void Blocking(ILogger logger, int count);

        [LoggerMessage(LogLevel.Debug, "sd_journal_next. Result was {Result}")]
        public static partial void NextResult(ILogger logger, int result);
    }
}
