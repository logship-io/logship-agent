// <copyright file="WindowsProcessPortInfoUtils.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Services.Sources.Common;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Logship.Agent.Core.Services.Sources.Windows
{
    internal sealed class WindowsProcessPortInfoUtils
    {
        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(nint pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int tcpTableType, uint reserved);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedUdpTable(nint pUdpTable, ref int dwOutBufLen, bool sort, int ipVersion, UDP_TABLE_CLASS tblClass, uint reserved);

        [UnconditionalSuppressMessage("AOT", "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling.", Justification = "<Pending>")]
        public static IReadOnlyList<SystemProcessInformationService.ProcessPortInfo> GetTcpPortInfo()
        {
            unsafe
            {
                Dictionary<int, string> portsAndProcesses = new Dictionary<int, string>();
                int size = 0;
                _ = GetExtendedTcpTable(nint.Zero, ref size, true, 2, 5, 0);

                nint tcpTable = Marshal.AllocHGlobal(size);
                try
                {
                    uint result = GetExtendedTcpTable(tcpTable, ref size, true, 2, 5, 0);
                    if (result != 0)
                    {
                        return Array.Empty<SystemProcessInformationService.ProcessPortInfo>();
                    }

                    MIB_TCPTABLE_OWNER_PID? tcpTableOwnerPid = *(MIB_TCPTABLE_OWNER_PID?*)tcpTable;
                    if (tcpTableOwnerPid == null)
                    {
                        return Array.Empty<SystemProcessInformationService.ProcessPortInfo>();
                    }

                    return tcpTableOwnerPid.Value.Rows.Select(
                        t => new SystemProcessInformationService.ProcessPortInfo(t.dwLocalAddr, t.LocalPort, t.dwRemoteAddr, t.dwRemotePort, t.dwOwningPid)).ToArray();
                }
                finally
                {
                    Marshal.FreeHGlobal(tcpTable);
                }
            }
            
        }

        public static IReadOnlyList<SystemProcessInformationService.ProcessPortInfo> GetUdpPortInfo()
        {
            unsafe
            {
                var udpTable = nint.Zero;
                try
                {
                    var bufferSize = 0;
                    _ = GetExtendedUdpTable(nint.Zero, ref bufferSize, true, 2 /*AF_INET*/, UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID, 0);
                    udpTable = Marshal.AllocHGlobal(bufferSize);
                    var result = GetExtendedUdpTable(udpTable, ref bufferSize, true, 2 /*AF_INET*/, UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID, 0);
                    if (result != 0)
                    {
                        throw new WindowsInteropException($"Failed to retrieve UDP table with error code {result}");
                    }

                    var table = *(MIB_UDPTABLE_OWNER_PID*)udpTable;
                    var resultArray = new SystemProcessInformationService.ProcessPortInfo[table.Rows.Length];
                    for (int i = 0; i < resultArray.Length; i++)
                    {
                        resultArray[i] = new SystemProcessInformationService.ProcessPortInfo(table.Rows[i].localAddr, table.Rows[i].LocalPort, 0, 0, table.Rows[i].owningPid);
                    }

                    return resultArray;
                }
                finally
                {
                    if (udpTable != nint.Zero)
                    {
                        Marshal.FreeHGlobal(udpTable);
                    }
                }
            }
            
        }

        public sealed class WindowsInteropException : Exception
        {
            public WindowsInteropException()
            {
            }

            public WindowsInteropException(string? message) : base(message)
            {
            }

            public WindowsInteropException(string? message, Exception? innerException) : base(message, innerException)
            {
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_TCPROW_OWNER_PID
        {
            public uint dwState;
            public uint dwLocalAddr;
            public uint dwLocalPort;
            public uint dwRemoteAddr;
            public uint dwRemotePort;
            public uint dwOwningPid;

            public int ProcessId { get { return Convert.ToInt32(dwOwningPid); } }
            public ushort LocalPort { get { return Convert.ToUInt16(dwLocalPort >> 8 & 0xff | dwLocalPort << 8 & 0xff00); } }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_TCPTABLE_OWNER_PID
        {
            public uint dwNumEntries;
            private MIB_TCPROW_OWNER_PID table;

            public MIB_TCPROW_OWNER_PID[] Rows
            {
                get
                {
                    MIB_TCPROW_OWNER_PID[] result = new MIB_TCPROW_OWNER_PID[dwNumEntries];
                    var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<MIB_TCPROW_OWNER_PID>() * (int)dwNumEntries);
                    Marshal.StructureToPtr(table, ptr, false);
                    for (int i = 0; i < dwNumEntries; ++i)
                    {
                        result[i] = (MIB_TCPROW_OWNER_PID)Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(ptr + (i * Marshal.SizeOf<MIB_TCPROW_OWNER_PID>()))!;
                    }

                    return result;
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_UDPROW_OWNER_PID
        {
            public uint localAddr;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] localPort;
            public uint owningPid;

            public ushort LocalPort => BitConverter.ToUInt16(new byte[2] { localPort[1], localPort[0] }, 0);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_UDPTABLE_OWNER_PID
        {
            public uint dwNumEntries;
            private readonly nint table;

            public MIB_UDPROW_OWNER_PID[] Rows
            {
                get
                {
                    unsafe
                    {
                        MIB_UDPROW_OWNER_PID[] result = new MIB_UDPROW_OWNER_PID[dwNumEntries];
                        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<MIB_UDPROW_OWNER_PID>() * (int)dwNumEntries);
                        Marshal.StructureToPtr(table, ptr, false);
                        for (int i = 0; i < dwNumEntries; ++i)
                        {
                            result[i] = (MIB_UDPROW_OWNER_PID)Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(ptr + (i * Marshal.SizeOf<MIB_UDPROW_OWNER_PID>()))!;
                        }
                        return result;
                    }
                    
                }
            }

        }

        private enum UDP_TABLE_CLASS
        {
            UDP_TABLE_BASIC,
            UDP_TABLE_OWNER_PID,
            UDP_TABLE_OWNER_MODULE
        }
    }
}

