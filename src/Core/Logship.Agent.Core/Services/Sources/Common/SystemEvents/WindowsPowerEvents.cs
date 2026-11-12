using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Logship.Agent.Core.Services.Sources.Common.SystemEvents;

internal sealed class WindowsSuspendEvents : IPowerEventSource
{
    public IDisposable Subscribe(Action<PowerNotice> notify) => new Registration(notify);
    private sealed class Registration : IDisposable
    {
        private readonly PowerCallback callback;
        private IntPtr handle;
        internal Registration(Action<PowerNotice> notify)
        {
            callback = (_, type, _) =>
            {
                try
                {
                    var action = type == 4 ? "Suspending" : type is 7 or 18 ? "Resumed" : null;
                    if (action != null) notify(new(action, DateTimeOffset.UtcNow));
                }
                catch (Exception ex) { try { notify(new("", DateTimeOffset.UtcNow, Failure: ex)); } catch { } }
                return 0;
            };
            var parameters = new Parameters { Callback = Marshal.GetFunctionPointerForDelegate(callback) };
            var error = PowerRegisterSuspendResumeNotification(2, ref parameters, out handle); // DEVICE_NOTIFY_CALLBACK
            if (error != 0) throw new Win32Exception((int)error);
        }
        public void Dispose()
        {
            if (handle != IntPtr.Zero) { _ = PowerUnregisterSuspendResumeNotification(handle); handle = IntPtr.Zero; }
            GC.KeepAlive(callback);
        }
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint PowerCallback(IntPtr context, uint type, IntPtr setting);
    [StructLayout(LayoutKind.Sequential)] private struct Parameters { public IntPtr Callback, Context; }
    [DllImport("powrprof.dll")] private static extern uint PowerRegisterSuspendResumeNotification(uint flags, ref Parameters parameters, out IntPtr handle);
    [DllImport("powrprof.dll")] private static extern uint PowerUnregisterSuspendResumeNotification(IntPtr handle);
}

internal sealed class WindowsShutdownEvents : IPowerEventSource
{
    public IDisposable Subscribe(Action<PowerNotice> notify) => new Registration(notify);
    private sealed class Registration : IDisposable
    {
        private readonly EventCallback callback;
        private IntPtr handle;
        internal Registration(Action<PowerNotice> notify)
        {
            callback = (action, context, evt) =>
            {
                try
                {
                    if (action == 0) throw new Win32Exception(evt.ToInt32());
                    _ = EvtRender(IntPtr.Zero, evt, 1, 0, IntPtr.Zero, out var bytes, out _);
                    if (bytes <= 0 || bytes > 1024 * 1024) throw new IOException("Invalid shutdown event XML size.");
                    var buffer = Marshal.AllocHGlobal(bytes);
                    try
                    {
                        if (!EvtRender(IntPtr.Zero, evt, 1, bytes, buffer, out _, out _)) throw new Win32Exception(Marshal.GetLastPInvokeError());
                        notify(new("ShutdownRequested", DateTimeOffset.UtcNow, Marshal.PtrToStringUni(buffer)));
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
                catch (Exception ex) { try { notify(new("", DateTimeOffset.UtcNow, Failure: ex)); } catch { } }
                return 0;
            };
            handle = EvtSubscribe(IntPtr.Zero, IntPtr.Zero, "System", "*[System[Provider[@Name='User32'] and EventID=1074]]",
                IntPtr.Zero, IntPtr.Zero, callback, 1); // EvtSubscribeToFutureEvents
            if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        public void Dispose()
        {
            if (handle != IntPtr.Zero) { _ = EvtClose(handle); handle = IntPtr.Zero; }
            GC.KeepAlive(callback);
        }
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint EventCallback(uint action, IntPtr context, IntPtr evt);
    [DllImport("wevtapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr EvtSubscribe(IntPtr session, IntPtr signal, string channel, string query, IntPtr bookmark, IntPtr context, EventCallback callback, uint flags);
    [DllImport("wevtapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EvtRender(IntPtr context, IntPtr evt, uint flags, int size, IntPtr buffer, out int used, out int count);
    [DllImport("wevtapi.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EvtClose(IntPtr handle);
}
