using System.Runtime.InteropServices;

namespace Logship.Agent.Core.Services.Sources.Linux
{
    internal static class Interop
    {
        private const string LibSystemD = "libsystemd.so.0";

        /// <summary>
        /// Opens the journal for reading.
        /// </summary>
        /// <returns>Returns 0 if successful, or a negative error code if an error occurred.</returns>
        [DllImport(LibSystemD, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sd_journal_open(out nint journal, int flags);

        /// <summary>
        /// Blocks until new journal data is available.
        /// </summary>
        /// <param name="timeout_usec">The timeout for waiting, in microseconds. A value of -1 means wait indefinitely.</param>
        /// <returns>Returns 1 if there is new data available, 0 if the timeout expired, or a negative error code if an error occurred.</returns>
        [DllImport(LibSystemD, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sd_journal_wait(nint journal, int timeout_usec);

        /// <summary>
        /// Closes the journal.
        /// </summary>
        /// <returns>Returns 0 if successful, or a negative error code if an error occurred.</returns>
        [DllImport(LibSystemD, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sd_journal_close(nint journal);

        /// <summary>
        /// Advances to the next journal entry.
        /// </summary>
        /// <returns>Returns 1 if successful, 0 if there are no more entries, or a negative error code if an error occurred.</returns>
        [DllImport(LibSystemD, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sd_journal_next(nint journal);

        /// <summary>
        /// Advances to the previous journal entry.
        /// </summary>
        /// <returns>Returns 1 if successful, 0 if there are no more entries, or a negative error code if an error occurred.</returns>
        [DllImport(LibSystemD, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sd_journal_previous(nint journal);

        /// <summary>
        /// Seeks to a specific journal entry using its cursor value.
        /// </summary>
        /// <param name="cursor">The cursor value of the entry to seek to.</param>
        /// <returns>Returns 0 if successful, or a negative error code if an error occurred.</returns>
        [DllImport(LibSystemD, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sd_journal_seek(string cursor);

        /// <summary>
        /// Gets the value of a specific field in the current journal entry.
        /// </summary>
        /// <param name="field">The name of the field to retrieve.</param>
        /// <param name="data">A pointer to the buffer to store the field data.</param>
        /// <param name="length">The length of the buffer.</param>
        /// <returns>Returns the length of the field data if successful, or a negative error code if an error occurred.</returns>
        [DllImport(LibSystemD, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sd_journal_get_data(nint journal, string field, out nint data, out nuint length);

        /// <summary>
        /// Enumerates all valid fields in the current journal entry.
        /// </summary>
        /// <param name="data">A pointer to the buffer to store the field data.</param>
        /// <param name="length">The length of the buffer.</param>
        /// <returns>Returns 1 if there are more fields, 0 if there are no more fields, or a negative error code if an error occurred.</returns>
        [DllImport(LibSystemD, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sd_journal_enumerate_available_data(nint journal, out nuint data, out nuint length);

        /// <summary>
        /// Gets the file descriptor of the current journal.
        /// </summary>
        /// <param name="fd">The file descriptor of the journal.</param>
        /// <param name="real_time">If non-zero, get the file descriptor of the real-time journal. Otherwise, get the file descriptor of the system journal.</param>
        /// <returns>Returns 0 if successful, or a negative error code if an error occurred.</returns>
        [DllImport(LibSystemD, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sd_journal_get_fd(out int fd, int real_time);

        /// <summary>
        /// Seeks to the beginning of the journal.
        /// </summary>
        /// <returns>Returns 0 if successful, or a negative error code if an error occurred.</returns>
        [DllImport(LibSystemD, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sd_journal_seek_head(nint journal);

        /// <summary>
        /// Seeks to the end of the journal.
        /// </summary>
        /// <returns>Returns 0 if successful, or a negative error code if an error occurred.</returns>
        [DllImport(LibSystemD, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sd_journal_seek_tail(nint journal);

        // <summary>
        /// Waits for an event on a file descriptor or a timeout to occur.
        /// </summary>
        /// <param name="fds">An array of pollfd structures specifying the file descriptors and events to monitor.</param>
        /// <param name="nfds">The number of elements in the fds array.</param>
        /// <param name="timeout">The maximum amount of time to wait, in milliseconds. A value of -1 means wait indefinitely.</param>
        /// <returns>Returns the number of file descriptors that have events or errors, 0 if the timeout expired, or a negative error code if an error occurred.</returns>
        [DllImport("libc.so.6")]
        internal static extern int poll(PollFd[] fds, uint nfds, int timeout);

        /// <summary>
        /// The pollfd structure used by the poll function to specify the file descriptor and events to monitor.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct PollFd
        {
            public int fd;          // File descriptor to monitor
            public short events;    // Events to monitor (POLLIN, POLLOUT, POLLERR, etc.)
            public short revents;   // Returned events
        }

        public static void Throw(int errorCode, string message)
        {
            throw new LinuxInteropException($"Linux interop error {errorCode}. {message}. {GetJournalErrorMessage(errorCode)}");
        }

        private static string GetJournalErrorMessage(int errorCode)
        {
            string errorMessage = errorCode switch
            {
                0 => "Success",
                -12 => "Memory allocation failed (ENOMEM)",
                -99 => "The requested field name or value does not exist in the journal entry (EADDRNOTAVAIL)",
                -17 => "A journal cursor with the specified name already exists (EEXIST)",
                -2 => "The requested journal file or directory does not exist (ENOENT)",
                -22 => "The input parameters to the function were invalid (EINVAL)",
                -16 => "The requested journal file or directory is currently locked or in use by another process (EBUSY)",
                -6 => "The journal file has been rotated or deleted since the cursor was created (ENXIO)",
                -32 => "The journal pipe was closed unexpectedly (EPIPE)",
                -3 => "The specified journal cursor does not exist (ESRCH)",
                _ => $"Unknown error ({errorCode})",
            };
            return errorMessage;
        }

        public sealed class LinuxInteropException : Exception
        {
            public LinuxInteropException()
            {
            }

            public LinuxInteropException(string? message) : base(message)
            {
            }

            public LinuxInteropException(string? message, Exception? innerException) : base(message, innerException)
            {
            }
        }
    }
}
