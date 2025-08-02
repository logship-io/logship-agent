// <copyright file="LinuxProcessPortInfoUtils.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Services.Sources.Common;

namespace Logship.Agent.Core.Services.Sources.Linux
{
    internal static class LinuxProcessPortInfoUtils
    {
        public static async Task<IReadOnlyList<SystemProcessInformationService.ProcessPortInfo>> ListProcessesUsingTcpPortsAsync(CancellationToken token)
        {
            var tcpLines = File.ReadAllLinesAsync("/proc/net/tcp", token);
            Dictionary<long, uint> inodeToPid = GetInodePidMap();

            var processPorts = new List<SystemProcessInformationService.ProcessPortInfo>();
            foreach (var line in await tcpLines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string[] fields = line.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length <= 9)
                {
                    continue;
                }

                bool TryParseAddr(string input, out uint addr, out uint port)
                {
                    addr = port = 0;
                    var parts = input.Split(":", StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2
                        || false == uint.TryParse(parts[0], System.Globalization.NumberStyles.HexNumber, System.Globalization.NumberFormatInfo.InvariantInfo, out addr))
                    {
                        return false;
                    }

                    return uint.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, System.Globalization.NumberFormatInfo.InvariantInfo, out port);
                }

                if (false == TryParseAddr(fields[1], out var localAddr, out var localPort)
                    || false == TryParseAddr(fields[2], out var remoteAddr, out var remotePort)
                    || false == int.TryParse(fields[9], out var inode)
                    || false == inodeToPid.ContainsKey(inode))
                {
                    continue;
                }

                processPorts.Add(new SystemProcessInformationService.ProcessPortInfo(localAddr, localPort, remoteAddr, remotePort, inodeToPid[inode]));
            }

            return processPorts;
        }

        private static readonly char[] separator = [' '];

        public static async Task<IReadOnlyList<SystemProcessInformationService.ProcessPortInfo>> ListProcessesUsingUdpPortsAsync(CancellationToken token)
        {
            var udpLines = File.ReadAllLinesAsync("/proc/net/udp", token);
            Dictionary<long, uint> inodeToPid = GetInodePidMap();

            var processPorts = new List<SystemProcessInformationService.ProcessPortInfo>();
            foreach (var line in await udpLines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string[] fields = line.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length <= 9)
                {
                    continue;
                }

                bool TryParseAddr(string input, out uint addr, out uint port)
                {
                    addr = port = 0;
                    var parts = input.Split(":", StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2
                        || false == uint.TryParse(parts[0], System.Globalization.NumberStyles.HexNumber, System.Globalization.NumberFormatInfo.InvariantInfo, out addr))
                    {
                        return false;
                    }

                    return uint.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, System.Globalization.NumberFormatInfo.InvariantInfo, out port);
                }

                if (false == TryParseAddr(fields[1], out var localAddr, out var localPort)
                    || false == TryParseAddr(fields[2], out var remoteAddr, out var remotePort)
                    || false == int.TryParse(fields[9], out var inode)
                    || false == inodeToPid.ContainsKey(inode))
                {
                    continue;
                }

                processPorts.Add(new SystemProcessInformationService.ProcessPortInfo(localAddr, localPort, remoteAddr, remotePort, inodeToPid[inode]));
            }

            return processPorts;
        }

        private static Dictionary<long, uint> GetInodePidMap()
        {
            var inodeToPid = new Dictionary<long, uint>();
            foreach (var dir in Directory.GetDirectories("/proc"))
            {
                if (uint.TryParse(Path.GetFileName(dir), out var pid))
                {
                    var inodes = GetPidInodes(pid);
                    foreach (var inode in inodes)
                    {
                        inodeToPid[inode] = pid;
                    }
                }
            }

            return inodeToPid;
        }

        private static HashSet<long> GetPidInodes(uint pid)
        {
            var inodes = new HashSet<long>();
            string fdPath = $"/proc/{pid}/fd";
            string[] files = Directory.GetFiles(fdPath);
            foreach (string file in files)
            {
                string target = GetSymbolicLinkTarget(file);
                if (target?.StartsWith("socket:[", StringComparison.Ordinal) == true)
                {
                    int index = target.IndexOf('[') + 1;
                    int length = target.IndexOf(']') - index;
                    string inodeString = target.Substring(index, length);
                    if (long.TryParse(inodeString, out var result))
                    {
                        inodes.Add(result);
                    }
                }
            }

            return inodes;
        }

        private static string GetSymbolicLinkTarget(string path)
        {
            string target = Path.GetFullPath(path);
            if (File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
            {
                var info = new FileInfo(target);
                target = info.LinkTarget!;
            }

            return target;
        }
    }
}

