using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;

namespace Logship.Agent.Core.Services.Sources.Common.SystemEvents;

internal static class WindowsNetworkEvents
{
    internal static IReadOnlyList<SystemEntity> Routes()
    {
        Check(GetIpForwardTable2(0, out var table));
        try
        {
            var count = Marshal.ReadInt32(table);
            var result = new List<SystemEntity>();
            for (var i = 0; i < count; i++) result.Add(Route(table + 8 + i * 104));
            return result;
        }
        finally { FreeMibTable(table); }
    }

    internal static SystemEntity Route(IntPtr row) => NetworkEventProvider.Route(Marshal.ReadInt32(row, 8),
        Address(row + 12), Marshal.ReadByte(row, 40), Address(row + 44), unchecked((uint)Marshal.ReadInt32(row, 84)));

    private static IPAddress Address(IntPtr socketAddress)
    {
        var family = Marshal.ReadInt16(socketAddress);
        if (family is not (2 or 23)) throw new IOException("Unexpected Windows address family.");
        var bytes = new byte[family == 2 ? 4 : 16];
        Marshal.Copy(socketAddress + (family == 2 ? 4 : 8), bytes, 0, bytes.Length);
        return new(bytes);
    }

    internal static IDisposable Subscribe(Action<SystemChange> notify) => new Registration(notify);

    private sealed class Registration : IDisposable
    {
        private readonly ChangeCallback interfaces, addresses, routes;
        private IntPtr interfaceHandle, addressHandle, routeHandle;
        internal Registration(Action<SystemChange> notify)
        {
            interfaces = (_, row, type) => Safe(() =>
            {
                if (row != IntPtr.Zero && type is 1 or 2)
                    notify(new(NetworkEventProvider.Interface(Marshal.ReadInt32(row, 16)), type == 1, DateTimeOffset.UtcNow));
                notify(SystemChange.Signal()); // Query operational status rather than assuming administrative UP means link UP.
            }, notify);
            addresses = (_, row, type) => Safe(() =>
            {
                if (row == IntPtr.Zero) return;
                var copy = Marshal.AllocHGlobal(80);
                try
                {
                    var bytes = new byte[80]; Marshal.Copy(row, bytes, 0, bytes.Length); Marshal.Copy(bytes, 0, copy, bytes.Length);
                    if (type != 2 && GetUnicastIpAddressEntry(copy) != 0) { notify(SystemChange.Signal()); return; }
                    notify(new(NetworkEventProvider.Address(Marshal.ReadInt32(copy, 40), Address(copy), Marshal.ReadByte(copy, 60)), type != 2, DateTimeOffset.UtcNow));
                }
                finally { Marshal.FreeHGlobal(copy); }
            }, notify);
            routes = (_, row, type) => Safe(() =>
            {
                if (row == IntPtr.Zero) return;
                // GetIpForwardEntry2 enriches add/update rows; deletion metadata comes from cached state.
                var copy = Marshal.AllocHGlobal(104);
                try
                {
                    var bytes = new byte[104]; Marshal.Copy(row, bytes, 0, bytes.Length); Marshal.Copy(bytes, 0, copy, bytes.Length);
                    if (type != 2 && GetIpForwardEntry2(copy) != 0) { notify(SystemChange.Signal()); return; }
                    notify(new(Route(copy), type != 2, DateTimeOffset.UtcNow));
                }
                finally { Marshal.FreeHGlobal(copy); }
            }, notify);
            try
            {
                Check(NotifyIpInterfaceChange(0, interfaces, IntPtr.Zero, false, out interfaceHandle));
                Check(NotifyUnicastIpAddressChange(0, addresses, IntPtr.Zero, false, out addressHandle));
                Check(NotifyRouteChange2(0, routes, IntPtr.Zero, false, out routeHandle));
            }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            if (interfaceHandle != IntPtr.Zero) { _ = CancelMibChangeNotify2(interfaceHandle); interfaceHandle = IntPtr.Zero; }
            if (addressHandle != IntPtr.Zero) { _ = CancelMibChangeNotify2(addressHandle); addressHandle = IntPtr.Zero; }
            if (routeHandle != IntPtr.Zero) { _ = CancelMibChangeNotify2(routeHandle); routeHandle = IntPtr.Zero; }
            GC.KeepAlive(interfaces); GC.KeepAlive(addresses); GC.KeepAlive(routes);
        }
    }
    private static void Safe(Action action, Action<SystemChange> notify)
    {
        try { action(); }
        catch (Exception ex) { try { notify(new(null, false, DateTimeOffset.UtcNow, ex)); } catch { } }
    }
    private static void Check(uint error) { if (error != 0) throw new Win32Exception((int)error); }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void ChangeCallback(IntPtr context, IntPtr row, int type);
    [DllImport("iphlpapi.dll")] private static extern uint GetIpForwardTable2(ushort family, out IntPtr table);
    [DllImport("iphlpapi.dll")] private static extern uint GetIpForwardEntry2(IntPtr row);
    [DllImport("iphlpapi.dll")] private static extern uint GetUnicastIpAddressEntry(IntPtr row);
    [DllImport("iphlpapi.dll")] private static extern void FreeMibTable(IntPtr table);
    [DllImport("iphlpapi.dll")] private static extern uint NotifyIpInterfaceChange(ushort family, ChangeCallback callback, IntPtr context, [MarshalAs(UnmanagedType.U1)] bool initial, out IntPtr handle);
    [DllImport("iphlpapi.dll")] private static extern uint NotifyUnicastIpAddressChange(ushort family, ChangeCallback callback, IntPtr context, [MarshalAs(UnmanagedType.U1)] bool initial, out IntPtr handle);
    [DllImport("iphlpapi.dll")] private static extern uint NotifyRouteChange2(ushort family, ChangeCallback callback, IntPtr context, [MarshalAs(UnmanagedType.U1)] bool initial, out IntPtr handle);
    [DllImport("iphlpapi.dll")] private static extern uint CancelMibChangeNotify2(IntPtr handle);
}
