using System.Runtime.InteropServices;

namespace Logship.Agent.Core.Services.Sources.Common.SystemEvents;

internal sealed class LinuxPowerEvents : IPowerEventSource
{
    public IDisposable Subscribe(Action<PowerNotice> notify) => new Registration(notify);

    private sealed class Registration : IDisposable
    {
        private readonly BusCallback callback;
        private readonly NativeWorker worker;
        private IntPtr bus, slot;
        internal Registration(Action<PowerNotice> notify)
        {
            callback = (message, _, _) =>
            {
                try
                {
                    var member = Marshal.PtrToStringUTF8(sd_bus_message_get_member(message));
                    if (member is not ("PrepareForSleep" or "PrepareForShutdown")) return 0;
                    Check(sd_bus_message_read_basic(message, (byte)'b', out var active));
                    var action = member == "PrepareForSleep" ? active != 0 ? "Suspending" : "Resumed"
                        : member == "PrepareForShutdown" && active != 0 ? "ShutdownRequested" : null;
                    if (action != null) notify(new(action, DateTimeOffset.UtcNow));
                }
                catch (Exception ex) { try { notify(new("", DateTimeOffset.UtcNow, Failure: ex)); } catch { } }
                return 0;
            };
            try
            {
                Check(sd_bus_open_system(out bus));
                Check(sd_bus_set_method_call_timeout(bus, 1000000));
                // Verify logind exists rather than silently waiting on an absent service.
                Check(sd_bus_call_method(bus, "org.freedesktop.login1", "/org/freedesktop/login1", "org.freedesktop.DBus.Peer", "Ping", IntPtr.Zero, out var reply, ""));
                _ = sd_bus_message_unref(reply);
                Check(sd_bus_add_match(bus, out slot,
                    "type='signal',sender='org.freedesktop.login1',path='/org/freedesktop/login1',interface='org.freedesktop.login1.Manager'", callback, IntPtr.Zero));
                worker = new NativeWorker(token =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        var result = sd_bus_process(bus, IntPtr.Zero);
                        Check(result);
                        if (result == 0) Check(sd_bus_wait(bus, 250000));
                    }
                }, ex => notify(new("", DateTimeOffset.UtcNow, Failure: ex)), Release);
            }
            catch { Release(); throw; }
        }
        public void Dispose() { worker.Dispose(); GC.KeepAlive(callback); }
        private void Release()
        {
            if (slot != IntPtr.Zero) { _ = sd_bus_slot_unref(slot); slot = IntPtr.Zero; }
            if (bus != IntPtr.Zero) { _ = sd_bus_unref(bus); bus = IntPtr.Zero; }
        }
    }
    private static void Check(int result) { if (result < 0) throw new IOException($"logind bus operation failed: {-result}."); }
    private const string Lib = "libsystemd.so.0";
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int BusCallback(IntPtr message, IntPtr data, IntPtr error);
#pragma warning disable CA2101 // sd-bus explicitly requires UTF-8 strings.
    [DllImport(Lib)] private static extern int sd_bus_open_system(out IntPtr bus);
    [DllImport(Lib)] private static extern int sd_bus_set_method_call_timeout(IntPtr bus, ulong timeout);
    [DllImport(Lib)] private static extern int sd_bus_add_match(IntPtr bus, out IntPtr slot, [MarshalAs(UnmanagedType.LPUTF8Str)] string match, BusCallback callback, IntPtr data);
    [DllImport(Lib)] private static extern int sd_bus_call_method(IntPtr bus, [MarshalAs(UnmanagedType.LPUTF8Str)] string destination, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string iface, [MarshalAs(UnmanagedType.LPUTF8Str)] string member, IntPtr error, out IntPtr reply, [MarshalAs(UnmanagedType.LPUTF8Str)] string types);
    [DllImport(Lib)] private static extern int sd_bus_process(IntPtr bus, IntPtr message);
    [DllImport(Lib)] private static extern int sd_bus_wait(IntPtr bus, ulong timeout);
    [DllImport(Lib)] private static extern int sd_bus_message_read_basic(IntPtr message, byte type, out int value);
    [DllImport(Lib)] private static extern IntPtr sd_bus_message_get_member(IntPtr message);
    [DllImport(Lib)] private static extern IntPtr sd_bus_message_unref(IntPtr message);
    [DllImport(Lib)] private static extern IntPtr sd_bus_slot_unref(IntPtr slot);
    [DllImport(Lib)] private static extern IntPtr sd_bus_unref(IntPtr bus);
#pragma warning restore CA2101
}
