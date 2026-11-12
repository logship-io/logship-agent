using System.Runtime.InteropServices;
using System.Text;

namespace Logship.Agent.Core.Services.Sources.Common.Usb;

internal sealed class WindowsUsbProvider : IUsbProvider
{
    internal static bool IsDevice(string id) => id.StartsWith("USB\\VID_", StringComparison.OrdinalIgnoreCase)
        && !id.Contains("&MI_", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<UsbDevice> Enumerate()
    {
        // CM_GETIDLIST_FILTER_ENUMERATOR | CM_GETIDLIST_FILTER_PRESENT
        for (var attempt = 0; attempt < 5; attempt++)
        {
            Check(CM_Get_Device_ID_List_SizeW(out var size, "USB", 0x101));
            var buffer = new char[size];
            var result = CM_Get_Device_ID_ListW("USB", buffer, size, 0x101);
            if (result == 0x1a) continue; // CR_BUFFER_SMALL: topology changed during enumeration.
            Check(result);
            return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Where(IsDevice).Distinct(StringComparer.OrdinalIgnoreCase).Select(Read).ToArray();
        }
        throw new IOException("USB topology changed repeatedly during enumeration.");
    }

    public UsbDevice Read(string id)
    {
        var parts = id.Split('\\');
        var hardware = parts.Length > 1 ? parts[1].Split('&') : [];
        string? Id(string prefix) => UsbDevice.HexId(hardware.FirstOrDefault(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..]);
        var found = CM_Locate_DevNodeW(out var node, id, 0) == 0;
        // CM_DEVCAP_UNIQUEID distinguishes real serial numbers from generated location IDs.
        var capabilities = found ? Property(node, 0x10) : null;
        var unique = capabilities is { Length: >= 4 } && (BitConverter.ToUInt32(capabilities) & 0x10) != 0;
        return new UsbDevice(id, Id("VID_"), Id("PID_"), found ? Text(node, 0xc) : null,
            found ? Text(node, 0xd) ?? Text(node, 1) : null,
            unique && parts.Length > 2 ? parts[2] : null, found ? Text(node, 0xe) : null);
    }

    private static byte[]? Property(uint node, uint property)
    {
        uint size = 0;
        if (CM_Get_DevNode_Registry_PropertyW(node, property, out _, null, ref size, 0) != 0x1a || size > 65536) return null;
        var buffer = new byte[size];
        return CM_Get_DevNode_Registry_PropertyW(node, property, out _, buffer, ref size, 0) == 0 ? buffer : null;
    }
    private static string? Text(uint node, uint property)
    {
        var bytes = Property(node, property);
        return bytes == null ? null : Encoding.Unicode.GetString(bytes).TrimEnd('\0');
    }

    public IDisposable Subscribe(Action<UsbChange> change, Action<Exception> failure) => new Registration(change, failure);
    public void Dispose() { }
    private static void Check(uint result) { if (result != 0) throw new IOException($"Configuration Manager failed: 0x{result:x}."); }

    private sealed class Registration : IDisposable
    {
        private readonly NotifyCallback callback;
        private IntPtr handle;
        internal Registration(Action<UsbChange> change, Action<Exception> failure)
        {
            callback = (_, _, action, data, size) =>
            {
                try
                {
                    if (action is not (7 or 8 or 9) || size < 10) return 0;
                    var id = Marshal.PtrToStringUni(data + 8, (int)Math.Min((size - 8) / 2, 200))?.Split('\0')[0];
                    if (id != null && IsDevice(id)) change(new UsbChange(new UsbDevice(id), action != 9, DateTimeOffset.UtcNow));
                }
                catch (Exception ex) { try { failure(ex); } catch { /* Never unwind into native code. */ } }
                return 0;
            };
            var filter = new NotifyFilter { Size = 416, Flags = 2, Type = 2, InstanceId = string.Empty };
            Check(CM_Register_Notification(ref filter, IntPtr.Zero, callback, out handle));
        }
        public void Dispose()
        {
            if (handle != IntPtr.Zero) { Check(CM_Unregister_Notification(handle)); handle = IntPtr.Zero; }
            GC.KeepAlive(callback);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyFilter
    {
        public uint Size, Flags, Type, Reserved;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 200)] public string InstanceId;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint NotifyCallback(IntPtr notification, IntPtr context, uint action, IntPtr data, uint size);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Get_Device_ID_List_SizeW(out uint size, string filter, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Get_Device_ID_ListW(string filter, [Out] char[] buffer, uint size, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Locate_DevNodeW(out uint node, string id, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_DevNode_Registry_PropertyW(uint node, uint property, out uint type, [Out] byte[]? buffer, ref uint size, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Register_Notification(ref NotifyFilter filter, IntPtr context, NotifyCallback callback, out IntPtr handle);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Unregister_Notification(IntPtr handle);
}
