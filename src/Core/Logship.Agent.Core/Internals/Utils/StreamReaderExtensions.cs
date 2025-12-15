namespace Logship.Agent.Core.Internals.Utils
{
    internal static class StreamReaderExtensions
    {
        /// <summary>
        /// Reads exactly the length of the buffer given.
        /// </summary>
        /// <param name="reader">The stream reader.</param>
        /// <param name="buffer">The buffer</param>
        /// <returns>True on success, false otherwise.</returns>
        public static async Task<bool> ReadExectlyAsync(this StreamReader reader, Memory<byte> buffer)
        {
            var remaining = buffer;
            while (remaining.Length > 0)
            {
                var read = reader.BaseStream.Read(remaining.Span);
                if (read == 0)
                {
                    return false;
                }
                remaining = remaining.Slice(read);
            }

            return true;
        }
    }
}
