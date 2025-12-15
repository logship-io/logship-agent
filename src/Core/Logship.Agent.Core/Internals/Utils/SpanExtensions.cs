namespace Logship.Agent.Core.Internals.Utils
{
    using System;

    internal static class SpanExtensions
    {
        public static bool TryNexToken(this ReadOnlySpan<char> span, char delimiter, out ReadOnlySpan<char> token)
        {
            var index = span.IndexOf(delimiter);
            if (-1 == index)
            {
                token = default;
                return false;
            }

            token = span.Slice(0, index);
            return true;
        }
    }
}
