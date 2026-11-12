using System.Buffers.Binary;
using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;

namespace Logship.Agent.Core.Services.Sources.Common.SystemEvents;

internal static class LinuxNetworkEvents
{
    internal static IReadOnlyList<SystemEntity> Routes()
    {
        var fd = Open(0);
        try
        {
            var request = new byte[28];
            BinaryPrimitives.WriteInt32LittleEndian(request, request.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(4), 26); // RTM_GETROUTE
            BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(6), 0x301); // REQUEST | DUMP
            BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(8), 1);
            if (send(fd, request, (nuint)request.Length, 0) < 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            var result = new List<SystemEntity>();
            var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
            var buffer = new byte[262144];
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (!NativePoll.Readable(fd)) continue;
                var length = Receive(fd, buffer);
                var done = ParseMessages(buffer.AsSpan(0, length), (type, payload) =>
                {
                    if (type == 24) result.AddRange(ParseRoutes(payload));
                });
                if (done) return result;
            }
            throw new IOException("Timed out reading the route table; refusing partial inventory.");
        }
        finally { _ = close(fd); }
    }

    internal static IDisposable Subscribe(Action<SystemChange> notify)
    {
        var fd = Open(0x551); // LINK, IPv4/IPv6 address and route multicast groups.
        return new NativeWorker(token =>
        {
            var buffer = new byte[262144];
            while (!token.IsCancellationRequested)
            {
                if (!NativePoll.Readable(fd)) continue;
                var count = Receive(fd, buffer);
                ParseMessages(buffer.AsSpan(0, count), (type, payload) =>
                {
                    var timestamp = DateTimeOffset.UtcNow;
                    if (type is 24 or 25)
                    {
                        foreach (var route in ParseRoutes(payload)) notify(new(route, type == 24, timestamp));
                    }
                    else if (type is 20 or 21)
                    {
                        var address = ParseAddress(payload);
                        if (address != null) notify(new(address, type == 20, timestamp));
                    }
                    else if (type is 16 or 17 && payload.Length >= 16)
                    {
                        var index = BinaryPrimitives.ReadInt32LittleEndian(payload[4..]);
                        var flags = BinaryPrimitives.ReadUInt32LittleEndian(payload[8..]);
                        var attrs = Attributes(payload[16..]);
                        var name = attrs.TryGetValue(3, out var bytes) ? System.Text.Encoding.UTF8.GetString(bytes).TrimEnd('\0') : null;
                        notify(new(NetworkEventProvider.Interface(index, name), type == 16, timestamp));
                        // IFF_RUNNING corresponds to an operational link; administrative UP alone is insufficient.
                        var up = attrs.TryGetValue(16, out var operational) && operational.Length > 0
                            ? operational[0] == 6 : (flags & 0x40) != 0;
                        notify(new(NetworkEventProvider.Link(index, name), type == 16 && up, timestamp));
                    }
                });
            }
        }, ex => notify(new(null, false, DateTimeOffset.UtcNow, ex)), () => { _ = close(fd); });
    }

    internal delegate void MessageHandler(ushort type, ReadOnlySpan<byte> payload);
    internal static bool ParseMessages(ReadOnlySpan<byte> bytes, MessageHandler handler)
    {
        var done = false;
        while (bytes.Length >= 16)
        {
            var length = BinaryPrimitives.ReadInt32LittleEndian(bytes);
            var type = BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]);
            var flags = BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]);
            if (length < 16 || length > bytes.Length) throw new IOException("Invalid netlink message length.");
            if ((flags & 0x10) != 0) throw new IOException("Netlink dump was interrupted; refusing partial inventory.");
            var payload = bytes[16..length];
            if (type == 2 && payload.Length >= 4 && BinaryPrimitives.ReadInt32LittleEndian(payload) != 0)
                throw new IOException("Netlink reported an error.");
            if (type == 4) throw new IOException("Netlink receive overflow.");
            if (type == 3)
            {
                if (payload.Length >= 4 && BinaryPrimitives.ReadInt32LittleEndian(payload) != 0)
                    throw new IOException("Netlink route dump failed.");
                done = true;
            }
            else handler(type, payload);
            var aligned = (length + 3) & ~3;
            bytes = aligned <= bytes.Length ? bytes[aligned..] : [];
        }
        return done;
    }

    internal static SystemEntity? ParseAddress(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8 || payload[0] is not (2 or 10)) return null;
        var attrs = Attributes(payload[8..]);
        if (!attrs.TryGetValue((ushort)(payload[0] == 2 ? 2 : 1), out var value) && !attrs.TryGetValue(1, out value)) return null;
        return NetworkEventProvider.Address(BinaryPrimitives.ReadInt32LittleEndian(payload[4..]), new IPAddress(value), payload[1]);
    }

    internal static IReadOnlyList<SystemEntity> ParseRoutes(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 12 || payload[0] is not (2 or 10)) return [];
        var addressSize = payload[0] == 2 ? 4 : 16;
        var attrs = Attributes(payload[12..]);
        var destination = new IPAddress(attrs.TryGetValue(1, out var dst) ? dst : new byte[addressSize]);
        var metric = UIntAttribute(attrs, 6);
        var table = attrs.ContainsKey(15) ? UIntAttribute(attrs, 15) : payload[4];
        var prefix = payload[1];
        var routeType = payload[7];
        if (attrs.TryGetValue(9, out var multipath))
        {
            var routes = new List<SystemEntity>();
            var remaining = multipath.AsSpan();
            while (remaining.Length >= 8)
            {
                var length = BinaryPrimitives.ReadUInt16LittleEndian(remaining);
                if (length < 8 || length > remaining.Length) throw new IOException("Invalid multipath route.");
                var nextAttrs = Attributes(remaining[8..length]);
                routes.Add(NetworkEventProvider.Route(BinaryPrimitives.ReadInt32LittleEndian(remaining[4..]), destination, prefix,
                    new IPAddress(nextAttrs.TryGetValue(5, out var hop) ? hop : new byte[addressSize]), metric, table, routeType));
                remaining = remaining[((length + 3) & ~3)..];
            }
            return routes;
        }
        return [NetworkEventProvider.Route((int)UIntAttribute(attrs, 4), destination, prefix,
            new IPAddress(attrs.TryGetValue(5, out var gateway) ? gateway : new byte[addressSize]), metric, table, routeType)];
    }

    internal static Dictionary<ushort, byte[]> Attributes(ReadOnlySpan<byte> bytes)
    {
        var result = new Dictionary<ushort, byte[]>();
        while (bytes.Length >= 4)
        {
            var length = BinaryPrimitives.ReadUInt16LittleEndian(bytes);
            var type = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(bytes[2..]) & 0x3fff);
            if (length < 4 || length > bytes.Length) throw new IOException("Invalid netlink attribute.");
            result[type] = bytes[4..length].ToArray();
            var aligned = (length + 3) & ~3;
            bytes = aligned <= bytes.Length ? bytes[aligned..] : [];
        }
        return result;
    }
    private static uint UIntAttribute(Dictionary<ushort, byte[]> attrs, ushort type) => attrs.TryGetValue(type, out var bytes) && bytes.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : 0;

    private static int Open(uint groups)
    {
        var fd = socket(16, 3 | 0x80000, 0); // AF_NETLINK, SOCK_RAW | CLOEXEC, NETLINK_ROUTE
        if (fd < 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        var address = new NetlinkAddress { Family = 16, Groups = groups };
        if (bind(fd, ref address, 12) == 0) return fd;
        var error = Marshal.GetLastPInvokeError();
        _ = close(fd);
        throw new Win32Exception(error);
    }
    private static int Receive(int fd, byte[] buffer)
    {
        var length = recv(fd, buffer, (nuint)buffer.Length, 0x20); // MSG_TRUNC reports full datagram length.
        if (length < 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        if (length > buffer.Length || length == 0) throw new IOException("Truncated or closed netlink socket.");
        return (int)length;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NetlinkAddress { public ushort Family, Padding; public uint Pid, Groups; }
    [DllImport("libc", SetLastError = true)] private static extern int socket(int domain, int type, int protocol);
    [DllImport("libc", SetLastError = true)] private static extern int bind(int fd, ref NetlinkAddress address, uint size);
    [DllImport("libc", SetLastError = true)] private static extern nint send(int fd, byte[] buffer, nuint size, int flags);
    [DllImport("libc", SetLastError = true)] private static extern nint recv(int fd, [Out] byte[] buffer, nuint size, int flags);
    [DllImport("libc")] private static extern int close(int fd);
}
