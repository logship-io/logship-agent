// <copyright file="ProcHelpers.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using System.Runtime.InteropServices;

namespace Logship.Agent.Core.Services.Sources.Linux.Proc
{
    internal static class ProcHelpers
    {
        public static int? ReadClockTicks(Stream auxiliaryVector)
        {
            const nuint AtNull = 0;
            const nuint AtClockTicks = 17;

            // Linux exposes each auxiliary-vector entry as two native-width, native-endian words.
            Span<byte> entry = stackalloc byte[2 * IntPtr.Size];
            while (auxiliaryVector.ReadAtLeast(entry, entry.Length, throwOnEndOfStream: false) == entry.Length)
            {
                var type = MemoryMarshal.Read<nuint>(entry);
                if (type == AtNull)
                {
                    break;
                }

                if (type == AtClockTicks)
                {
                    var ticks = MemoryMarshal.Read<nuint>(entry[IntPtr.Size..]);
                    return ticks is > 0 and <= int.MaxValue ? (int)ticks : null;
                }
            }

            return null;
        }
    }
}

