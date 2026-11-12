using System.Runtime.InteropServices;

namespace Logship.Agent.Core.Services.Sources.Common.SystemEvents;

internal sealed class NativeWorker : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly Task worker;
    private readonly Action release;
    private int disposed;
    internal NativeWorker(Action<CancellationToken> run, Action<Exception> failure, Action release)
    {
        this.release = release;
        worker = Task.Run(() => { try { run(stop.Token); } catch (Exception ex) { failure(ex); } });
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stop.Cancel();
        try { worker.GetAwaiter().GetResult(); }
        finally { release(); stop.Dispose(); }
    }
}

internal static class NativePoll
{
    internal static bool Readable(int fd, int timeout = 250)
    {
        var descriptor = new Descriptor { Fd = fd, Events = 1 };
        var result = poll(ref descriptor, 1, timeout);
        if (result < 0)
        {
            if (Marshal.GetLastPInvokeError() == 4) return false;
            throw new IOException("Native event socket poll failed.");
        }
        if ((descriptor.Returned & 0x38) != 0) throw new IOException("Native event socket closed.");
        return result > 0;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Descriptor { public int Fd; public short Events, Returned; }
    [DllImport("libc", SetLastError = true)] private static extern int poll(ref Descriptor descriptor, nuint count, int timeout);
}

internal static class UdevEvents
{
    internal static IDisposable Subscribe(string subsystem, Action<IntPtr> notify, Action<Exception> failure)
    {
        var context = udev_new();
        var monitor = IntPtr.Zero;
        try
        {
            if (context == IntPtr.Zero) throw new IOException("Cannot create udev context.");
            monitor = udev_monitor_new_from_netlink(context, "udev");
            if (monitor == IntPtr.Zero || udev_monitor_filter_add_match_subsystem_devtype(monitor, subsystem, null) < 0
                || udev_monitor_enable_receiving(monitor) < 0) throw new IOException("Cannot subscribe to udev events.");
            var fd = udev_monitor_get_fd(monitor);
            if (fd < 0) throw new IOException("Cannot obtain udev event descriptor.");
            return new NativeWorker(token =>
            {
                while (!token.IsCancellationRequested)
                {
                    if (!NativePoll.Readable(fd)) continue;
                    var device = udev_monitor_receive_device(monitor);
                    if (device == IntPtr.Zero) continue;
                    try { notify(device); }
                    finally { _ = udev_device_unref(device); }
                }
            }, failure, Release);
        }
        catch { Release(); throw; }

        void Release()
        {
            if (monitor != IntPtr.Zero) _ = udev_monitor_unref(monitor);
            if (context != IntPtr.Zero) _ = udev_unref(context);
        }
    }

    internal static string? Action(IntPtr device) => Marshal.PtrToStringUTF8(udev_device_get_action(device));
    internal static string? Name(IntPtr device) => Marshal.PtrToStringUTF8(udev_device_get_sysname(device));
    internal static string? Property(IntPtr device, string key) => Marshal.PtrToStringUTF8(udev_device_get_property_value(device, key));
    private const string Lib = "libudev.so.1";
#pragma warning disable CA2101 // libudev explicitly requires UTF-8 strings.
    [DllImport(Lib)] private static extern IntPtr udev_new();
    [DllImport(Lib)] private static extern IntPtr udev_unref(IntPtr context);
    [DllImport(Lib)] private static extern IntPtr udev_monitor_new_from_netlink(IntPtr context, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Lib)] private static extern int udev_monitor_filter_add_match_subsystem_devtype(IntPtr monitor, [MarshalAs(UnmanagedType.LPUTF8Str)] string subsystem, [MarshalAs(UnmanagedType.LPUTF8Str)] string? type);
    [DllImport(Lib)] private static extern int udev_monitor_enable_receiving(IntPtr monitor);
    [DllImport(Lib)] private static extern int udev_monitor_get_fd(IntPtr monitor);
    [DllImport(Lib)] private static extern IntPtr udev_monitor_receive_device(IntPtr monitor);
    [DllImport(Lib)] private static extern IntPtr udev_monitor_unref(IntPtr monitor);
    [DllImport(Lib)] private static extern IntPtr udev_device_unref(IntPtr device);
    [DllImport(Lib)] private static extern IntPtr udev_device_get_action(IntPtr device);
    [DllImport(Lib)] private static extern IntPtr udev_device_get_sysname(IntPtr device);
    [DllImport(Lib)] private static extern IntPtr udev_device_get_property_value(IntPtr device, [MarshalAs(UnmanagedType.LPUTF8Str)] string key);
#pragma warning restore CA2101
}
