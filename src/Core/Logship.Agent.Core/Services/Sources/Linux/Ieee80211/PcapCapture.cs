using System.Runtime.InteropServices;

namespace Logship.Agent.Core.Services.Sources.Linux.Ieee80211;

internal interface IIeee80211Capture : IDisposable
{
    int LinkType { get; }
    (byte[] Packet, DateTimeOffset Timestamp)? Read();
    uint Dropped { get; }
}

internal sealed partial class PcapCapture : IIeee80211Capture
{
    static PcapCapture()
    {
        NativeLibrary.SetDllImportResolver(typeof(PcapCapture).Assembly, (name, assembly, paths) =>
        {
            if (name != "logship-pcap") return 0;
            foreach (string candidate in new[] { "libpcap.so.0.8", "libpcap.so.1", "libpcap.so" })
                if (NativeLibrary.TryLoad(candidate, assembly, paths, out var library)) return library;
            throw new DllNotFoundException("Install the libpcap runtime package to enable Linux.80211.");
        });
    }
    private nint handle;
    public int LinkType { get; }
    public uint Dropped => Native.pcap_stats(handle, out var stats) == 0 ? stats.Dropped : 0;

    internal PcapCapture(string device)
    {
        if (IntPtr.Size != 8) throw new PlatformNotSupportedException("IEEE 802.11 capture requires 64-bit Linux.");
        byte[] error = new byte[256];
        handle = Native.pcap_create(device, error);
        if (handle == 0) throw new IOException(Error(error));
        try
        {
            Check(Native.pcap_set_snaplen(handle, 65535));
            Check(Native.pcap_set_promisc(handle, 1));
            Check(Native.pcap_set_timeout(handle, 100));
            Check(Native.pcap_set_immediate_mode(handle, 1));
            Check(Native.pcap_activate(handle));
            LinkType = Native.pcap_datalink(handle);
            if (LinkType != 105 && LinkType != 127) throw new IOException($"Unsupported IEEE 802.11 capture link type {LinkType}.");
            Check(Native.pcap_compile(handle, out var filter, "type mgt and (subtype beacon or subtype probe-req or subtype probe-resp)", 1, uint.MaxValue));
            try { Check(Native.pcap_setfilter(handle, ref filter)); }
            finally { Native.pcap_freecode(ref filter); }
            Check(Native.pcap_setnonblock(handle, 1, error));
        }
        catch { Dispose(); throw; }
    }

    public (byte[] Packet, DateTimeOffset Timestamp)? Read()
    {
        int result = Native.pcap_next_ex(handle, out var headerPointer, out var packetPointer);
        if (result == 0) return null;
        if (result < 0) throw new IOException(Marshal.PtrToStringUTF8(Native.pcap_geterr(handle)));
        var header = Marshal.PtrToStructure<PacketHeader>(headerPointer);
        if (header.CapturedLength > 65535 || header.CapturedLength < header.Length) return null;
        byte[] packet = new byte[header.CapturedLength];
        Marshal.Copy(packetPointer, packet, 0, packet.Length);
        return (packet, DateTimeOffset.FromUnixTimeSeconds(header.Seconds).AddTicks(header.Microseconds * 10));
    }

    private void Check(int result)
    {
        if (result < 0) throw new IOException(Marshal.PtrToStringUTF8(Native.pcap_geterr(handle)));
    }

    private static string Error(byte[] error) => System.Text.Encoding.UTF8.GetString(error).TrimEnd('\0');
    public void Dispose()
    {
        if (handle != 0) { Native.pcap_close(handle); handle = 0; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PacketHeader { public long Seconds; public long Microseconds; public uint CapturedLength; public uint Length; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Filter { public uint Length; public nint Instructions; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Stats { public uint Received; public uint Dropped; public uint InterfaceDropped; }

    private static partial class Native
    {
        private const string Library = "logship-pcap";
        [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)] internal static partial nint pcap_create(string device, [Out] byte[] error);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int pcap_set_snaplen(nint p, int value);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int pcap_set_promisc(nint p, int value);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int pcap_set_timeout(nint p, int value);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int pcap_set_immediate_mode(nint p, int value);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int pcap_activate(nint p);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int pcap_datalink(nint p);
        [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)] internal static partial int pcap_compile(nint p, out Filter filter, string expression, int optimize, uint mask);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int pcap_setfilter(nint p, ref Filter filter);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void pcap_freecode(ref Filter filter);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int pcap_setnonblock(nint p, int value, [Out] byte[] error);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int pcap_next_ex(nint p, out nint header, out nint packet);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint pcap_geterr(nint p);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int pcap_stats(nint p, out Stats stats);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void pcap_close(nint p);
    }
}
