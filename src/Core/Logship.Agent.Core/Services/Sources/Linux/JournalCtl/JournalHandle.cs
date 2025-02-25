using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace Logship.Agent.Core.Services.Sources.Linux.JournalCtl
{
    internal sealed class JournalHandle : SafeHandle
    {
        public JournalHandle() : base(nint.Zero, true)
        {
        }

        public override bool IsInvalid => handle == nint.Zero;

        public static JournalHandle Open(ILogger logger, int flags)
        {
            var handle = new JournalHandle();
            int result = Interop.sd_journal_open(out nint journal, flags);
            JournalCtlLog.JournalOpen(logger, result);
            if (result < 0)
            {
                Interop.Throw(result, "Couldn't open journal file");
            }

            handle.SetHandle(journal);
            return handle;
        }

        protected override bool ReleaseHandle()
        {
            if (handle != nint.Zero)
            {
                _ = Interop.sd_journal_close(handle);
            }

            return true;
        }
    }

    internal static partial class JournalCtlLog
    {
        [LoggerMessage(LogLevel.Debug, "sd_journal_open. Result was {Code}")]
        public static partial void JournalOpen(ILogger logger, int code);
    }
}
