using System.Threading.Channels;
using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Logship.Agent.Core.Services.Sources.Common.Usb;

internal sealed class UsbService : BaseInputService<UsbConfiguration>
{
    private readonly Func<IUsbProvider?> factory;

    public UsbService(IOptions<SourcesConfiguration> config, IEventBuffer buffer, ILogger<UsbService> logger)
        : this(config, buffer, logger, () => OperatingSystem.IsWindows() ? new WindowsUsbProvider()
            : OperatingSystem.IsLinux() ? new LinuxUsbProvider() : null) { }

    internal UsbService(IOptions<SourcesConfiguration> config, IEventBuffer buffer, ILogger<UsbService> logger,
        Func<IUsbProvider?> factory) : base(config.Value.USB, buffer, nameof(UsbService), logger)
        => this.factory = factory;

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var provider = factory();
        if (provider == null)
        {
            Logger.LogWarning("USB collection is supported only on Windows and Linux.");
            return;
        }
        var queue = Channel.CreateUnbounded<UsbChange>(new UnboundedChannelOptions { SingleReader = true });
        var failures = Channel.CreateUnbounded<Exception>();
        var state = new UsbState();
        IDisposable? subscription = null;
        Task<bool>? ready = null;
        var nextScan = DateTimeOffset.MinValue;
        var baseline = false;
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow >= nextScan)
                {
                    if (subscription == null)
                    {
                        try { subscription = provider.Subscribe(c => queue.Writer.TryWrite(c), e => failures.Writer.TryWrite(e)); }
                        catch (Exception ex) { Logger.LogWarning(ex, "USB native monitoring unavailable; using periodic inventory."); }
                    }
                    // Drain notifications before scanning so scans cannot erase brief attachments.
                    if (baseline) Drain();
                    try
                    {
                        var snapshot = provider.Enumerate();
                        var pending = new List<UsbChange>();
                        while (queue.Reader.TryRead(out var change)) pending.Add(Enrich(change));
                        foreach (var record in state.Reconcile(snapshot, DateTimeOffset.UtcNow, pending)) Buffer.Add(record);
                        baseline = true;
                    }
                    catch (Exception ex) { Logger.LogWarning(ex, "USB inventory failed; retaining previous device state."); }
                    nextScan = DateTimeOffset.UtcNow + Config.Interval;
                }
                Drain();
                if (failures.Reader.TryRead(out var failure))
                {
                    Logger.LogWarning(failure, "USB monitor failed; retrying at the next inventory interval.");
                    subscription?.Dispose();
                    subscription = null;
                }
                ready ??= queue.Reader.WaitToReadAsync(token).AsTask();
                using var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                var delay = Task.Delay(TimeSpan.FromMilliseconds(Math.Clamp((nextScan - DateTimeOffset.UtcNow).TotalMilliseconds, 1, 1000)), delayCancellation.Token);
                if (await Task.WhenAny(ready, delay) == ready)
                {
                    await ready;
                    ready = null;
                    delayCancellation.Cancel();
                }
            }
        }
        finally { subscription?.Dispose(); }

        void Drain()
        {
            while (queue.Reader.TryRead(out var change))
            {
                change = Enrich(change);
                var record = state.Change(change);
                if (record != null) Buffer.Add(record);
            }
        }

        UsbChange Enrich(UsbChange change)
        {
            if (!change.Connected) return change;
            try { return change with { Device = UsbState.Merge(change.Device, provider.Read(change.Device.DeviceId)) }; }
            catch (Exception ex)
            {
                if (Logger.IsEnabled(LogLevel.Debug)) Logger.LogDebug(ex, "USB metadata unavailable for {DeviceId}.", change.Device.DeviceId);
                return change;
            }
        }
    }
}
