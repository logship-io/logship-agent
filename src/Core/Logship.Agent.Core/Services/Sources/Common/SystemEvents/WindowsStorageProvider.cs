using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Logship.Agent.Core.Services.Sources.Common.SystemEvents;

internal sealed class WindowsStorageProvider : ISystemEventProvider
{
    private static readonly Guid DiskClass = new("53f56307-b6bf-11d0-94f2-00a0c91efb8b");
    private static readonly Guid VolumeClass = new("53f5630d-b6bf-11d0-94f2-00a0c91efb8b");

    internal static SystemEntity Device(string path, bool volume) => new((volume ? "Volume:" : "Disk:") + path.ToUpperInvariant(),
        volume ? "VolumeAdded" : "DiskAdded", volume ? "VolumeRemoved" : "DiskRemoved",
        new() { ["Kind"] = volume ? "Volume" : "Disk", ["DeviceId"] = path });

    public IReadOnlyList<SystemEntity> Snapshot()
    {
        var entities = EnumerateInterfaces(DiskClass, false).Concat(EnumerateInterfaces(VolumeClass, true)).ToList();
        var name = new char[1024];
        var find = FindFirstVolumeW(name, (uint)name.Length);
        if (find == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        try
        {
            do
            {
                var volume = Text(name);
                var label = new char[1024];
                var format = new char[1024];
                var metadata = GetVolumeInformationW(volume, label, (uint)label.Length, out _, out _, out _, format, (uint)format.Length);
                var paths = new char[1024];
                if (!GetVolumePathNamesForVolumeNameW(volume, paths, (uint)paths.Length, out var needed))
                {
                    var error = Marshal.GetLastPInvokeError();
                    if (error != 234) throw new Win32Exception(error);
                    paths = new char[needed];
                    if (!GetVolumePathNamesForVolumeNameW(volume, paths, needed, out _)) throw new Win32Exception(Marshal.GetLastPInvokeError());
                }
                foreach (var path in new string(paths).Split('\0', StringSplitOptions.RemoveEmptyEntries))
                {
                    var fields = new Dictionary<string, object> { ["Kind"] = "Mount", ["VolumeId"] = volume, ["MountLocation"] = path };
                    if (metadata) { fields["VolumeLabel"] = Text(label); fields["FileSystem"] = Text(format); }
                    entities.Add(new("Mount:" + volume.ToUpperInvariant() + ":" + path.ToUpperInvariant(), "Mounted", "Unmounted", fields));
                }
                Array.Clear(name);
            } while (FindNextVolumeW(find, name, (uint)name.Length));
            if (Marshal.GetLastPInvokeError() != 18) throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        finally { _ = FindVolumeClose(find); }
        return entities;
    }

    private static string Text(char[] buffer) => new string(buffer).Split('\0')[0];

    private static List<SystemEntity> EnumerateInterfaces(Guid classId, bool volume)
    {
        var set = SetupDiGetClassDevsW(ref classId, null, IntPtr.Zero, 0x12); // PRESENT | DEVICEINTERFACE
        if (set == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        var result = new List<SystemEntity>();
        try
        {
            for (uint i = 0; ; i++)
            {
                var info = new InterfaceData { Size = (uint)Marshal.SizeOf<InterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref classId, i, ref info))
                {
                    if (Marshal.GetLastPInvokeError() == 259) break;
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }
                _ = SetupDiGetDeviceInterfaceDetailW(set, ref info, IntPtr.Zero, 0, out var size, IntPtr.Zero);
                if (size < 6 || size > 65536) throw new IOException("Invalid storage interface path size.");
                var detail = Marshal.AllocHGlobal((int)size);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetailW(set, ref info, detail, size, out _, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastPInvokeError());
                    var path = Marshal.PtrToStringUni(detail + 4) ?? throw new IOException("Missing storage interface path.");
                    result.Add(Device(path, volume));
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { _ = SetupDiDestroyDeviceInfoList(set); }
        return result;
    }

    public IDisposable Subscribe(Action<SystemChange> notify) => new Registration(notify);

    private sealed class Registration : IDisposable
    {
        private readonly Callback callback;
        private IntPtr handle;
        internal Registration(Action<SystemChange> notify)
        {
            callback = (_, _, action, data, size) =>
            {
                try
                {
                    if (action is not (0 or 1) || size < 26) return 0;
                    var classId = Marshal.PtrToStructure<Guid>(data + 8);
                    if (classId != DiskClass && classId != VolumeClass) return 0;
                    var path = Marshal.PtrToStringUni(data + 24, checked((int)(size - 24) / 2))?.Split('\0')[0];
                    if (path != null) notify(new(Device(path, classId == VolumeClass), action == 0, DateTimeOffset.UtcNow));
                }
                catch (Exception ex) { notify(new(null, false, DateTimeOffset.UtcNow, ex)); }
                return 0;
            };
            var filter = new Filter { Size = 416, Flags = 1 }; // ALL_INTERFACE_CLASSES
            var result = CM_Register_Notification(ref filter, IntPtr.Zero, callback, out handle);
            if (result != 0) throw new IOException($"Storage notification registration failed: {result}.");
        }
        public void Dispose()
        {
            if (handle != IntPtr.Zero) { _ = CM_Unregister_Notification(handle); handle = IntPtr.Zero; }
            GC.KeepAlive(callback);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct InterfaceData { public uint Size; public Guid ClassId; public uint Flags; public UIntPtr Reserved; }
    [StructLayout(LayoutKind.Explicit, Size = 416)] private struct Filter { [FieldOffset(0)] public uint Size; [FieldOffset(4)] public uint Flags; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint Callback(IntPtr handle, IntPtr context, uint action, IntPtr data, uint size);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Register_Notification(ref Filter filter, IntPtr context, Callback callback, out IntPtr handle);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Unregister_Notification(IntPtr handle);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr SetupDiGetClassDevsW(ref Guid classId, string? enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid classId, uint index, ref InterfaceData info);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref InterfaceData info, IntPtr detail, uint size, out uint needed, IntPtr device);
    [DllImport("setupapi.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr FindFirstVolumeW([Out] char[] name, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FindNextVolumeW(IntPtr find, [Out] char[] name, uint size);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FindVolumeClose(IntPtr find);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetVolumePathNamesForVolumeNameW(string volume, [Out] char[] paths, uint size, out uint needed);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetVolumeInformationW(string volume, [Out] char[] label, uint labelSize, out uint serial, out uint maxComponent, out uint flags, [Out] char[] format, uint formatSize);
}
