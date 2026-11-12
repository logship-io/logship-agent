using System.Runtime.InteropServices;

namespace Logship.Agent.Core.Services.Sources.Common.Usb;

internal sealed class LinuxUsbProvider : IUsbProvider
{
    // Sysfs enumeration remains available when libudev or its event socket is unavailable.
    public IReadOnlyList<UsbDevice> Enumerate() => Directory.GetDirectories("/sys/bus/usb/devices")
        .Where(p => IsDeviceName(Path.GetFileName(p))).Select(Read).ToArray();

    internal static bool IsDeviceName(string name) => !name.StartsWith("usb", StringComparison.Ordinal)
        && !name.Contains(':') && name.Contains('-');

    public UsbDevice Read(string id)
    {
        string? Attribute(string name)
        {
            try { return File.ReadAllText(Path.Combine(id, name)).Trim(); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
        return new UsbDevice(id, UsbDevice.HexId(Attribute("idVendor")), UsbDevice.HexId(Attribute("idProduct")),
            Attribute("manufacturer"), Attribute("product"), Attribute("serial"), Path.GetFileName(id));
    }

    public IDisposable Subscribe(Action<UsbChange> change, Action<Exception> failure) => new Monitor(change, failure);
    public void Dispose() { }

    private sealed class Monitor : IDisposable
    {
        private IntPtr context, monitor;
        private readonly CancellationTokenSource stop = new();
        private readonly Task worker;

        internal Monitor(Action<UsbChange> change, Action<Exception> failure)
        {
            try
            {
                context = udev_new();
                if (context == IntPtr.Zero) throw new IOException("Cannot create udev context.");
                monitor = udev_monitor_new_from_netlink(context, "udev");
                if (monitor == IntPtr.Zero || udev_monitor_filter_add_match_subsystem_devtype(monitor, "usb", "usb_device") < 0
                    || udev_monitor_enable_receiving(monitor) < 0) throw new IOException("Cannot subscribe to udev USB events.");
                var fd = udev_monitor_get_fd(monitor);
                if (fd < 0) throw new IOException("Cannot obtain udev monitor descriptor.");
                worker = Task.Run(() =>
                {
                    try
                    {
                        while (!stop.IsCancellationRequested)
                        {
                            var descriptor = new PollDescriptor { Fd = fd, Events = 1 };
                            var result = poll(ref descriptor, 1, 250);
                            if (result < 0)
                            {
                                if (Marshal.GetLastPInvokeError() == 4) continue; // EINTR
                                throw new IOException("udev monitor poll failed.");
                            }
                            if ((descriptor.ReturnedEvents & 0x38) != 0) throw new IOException("udev monitor socket closed.");
                            if (result == 0) continue;
                            var device = udev_monitor_receive_device(monitor);
                            if (device == IntPtr.Zero) continue;
                            try
                            {
                                var action = Utf8(udev_device_get_action(device));
                                var name = Utf8(udev_device_get_sysname(device));
                                if (name == null || !IsDeviceName(name) || action is not ("add" or "remove")) continue;
                                string? Attr(string key) => Utf8(udev_device_get_sysattr_value(device, key));
                                var info = new UsbDevice("/sys/bus/usb/devices/" + name, UsbDevice.HexId(Attr("idVendor")),
                                    UsbDevice.HexId(Attr("idProduct")), Attr("manufacturer"), Attr("product"), Attr("serial"), name);
                                change(new UsbChange(info, action == "add", DateTimeOffset.UtcNow));
                            }
                            finally { udev_device_unref(device); }
                        }
                    }
                    catch (Exception ex) { failure(ex); }
                });
            }
            catch { Release(); stop.Dispose(); throw; }
        }

        public void Dispose()
        {
            stop.Cancel();
            worker.GetAwaiter().GetResult();
            Release();
            stop.Dispose();
        }
        private void Release()
        {
            if (monitor != IntPtr.Zero) { udev_monitor_unref(monitor); monitor = IntPtr.Zero; }
            if (context != IntPtr.Zero) { udev_unref(context); context = IntPtr.Zero; }
        }
    }

    private static string? Utf8(IntPtr value) => Marshal.PtrToStringUTF8(value);
    [StructLayout(LayoutKind.Sequential)] private struct PollDescriptor { public int Fd; public short Events, ReturnedEvents; }
    private const string Lib = "libudev.so.1";
    // libudev uses UTF-8, explicitly specified on each string parameter.
#pragma warning disable CA2101
    [DllImport("libc", SetLastError = true)] private static extern int poll(ref PollDescriptor descriptor, nuint count, int timeout);
    [DllImport(Lib)] private static extern IntPtr udev_new();
    [DllImport(Lib)] private static extern IntPtr udev_unref(IntPtr context);
    [DllImport(Lib)] private static extern IntPtr udev_monitor_new_from_netlink(IntPtr context, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Lib)] private static extern int udev_monitor_filter_add_match_subsystem_devtype(IntPtr monitor, [MarshalAs(UnmanagedType.LPUTF8Str)] string subsystem, [MarshalAs(UnmanagedType.LPUTF8Str)] string type);
    [DllImport(Lib)] private static extern int udev_monitor_enable_receiving(IntPtr monitor);
    [DllImport(Lib)] private static extern int udev_monitor_get_fd(IntPtr monitor);
    [DllImport(Lib)] private static extern IntPtr udev_monitor_receive_device(IntPtr monitor);
    [DllImport(Lib)] private static extern IntPtr udev_monitor_unref(IntPtr monitor);
    [DllImport(Lib)] private static extern IntPtr udev_device_get_action(IntPtr device);
    [DllImport(Lib)] private static extern IntPtr udev_device_get_sysname(IntPtr device);
    [DllImport(Lib)] private static extern IntPtr udev_device_get_sysattr_value(IntPtr device, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Lib)] private static extern IntPtr udev_device_unref(IntPtr device);
#pragma warning restore CA2101
}
