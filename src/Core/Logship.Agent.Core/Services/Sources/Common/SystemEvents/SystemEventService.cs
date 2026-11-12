using System.Threading.Channels;
using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Logship.Agent.Core.Services.Sources.Common.SystemEvents;

internal abstract class SystemEventService<TConfig>(TConfig? config, IEventBuffer buffer, string schema,
    ILogger logger, Func<ISystemEventProvider?> factory) : BaseInputService<TConfig>(config, buffer, typeof(TConfig).Name, logger)
    where TConfig : BaseIntervalInputConfiguration, new()
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var provider = factory();
        if (provider == null) { Logger.LogWarning("{Source} is supported only on Windows and Linux.", schema); return; }
        var queue = Channel.CreateUnbounded<SystemChange>(new() { SingleReader = true });
        var state = new EventState(schema);
        IDisposable? subscription = null;
        var nextScan = DateTimeOffset.MinValue;
        var nextRetry = DateTimeOffset.MinValue;
        var initialized = false;
        Task<bool>? ready = null;
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (subscription == null && DateTimeOffset.UtcNow >= nextRetry)
                {
                    try { subscription = provider.Subscribe(c => queue.Writer.TryWrite(c)); }
                    catch (Exception ex) { Logger.LogWarning(ex, "{Source} notifications unavailable; continuing reconciliation.", schema); }
                    nextRetry = DateTimeOffset.UtcNow.AddSeconds(30);
                }
                var pending = Drain();
                var signaled = pending.Count != 0;
                if (DateTimeOffset.UtcNow >= nextScan || signaled)
                {
                    if (initialized) foreach (var change in pending) Publish(state.Apply(change));
                    try
                    {
                        var snapshot = provider.Snapshot();
                        var duringScan = Drain();
                        if (!initialized) duringScan.InsertRange(0, pending);
                        Publish(state.Reconcile(snapshot, duringScan, signaled ? "Native" : "Reconciliation", DateTimeOffset.UtcNow));
                        initialized = true;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "{Source} scan failed; retaining previous state.", schema);
                        if (!initialized) foreach (var change in pending) Publish(state.Apply(change));
                    }
                    nextScan = DateTimeOffset.UtcNow + Config.Interval;
                }
                ready ??= queue.Reader.WaitToReadAsync(token).AsTask();
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
                var delay = Task.Delay(TimeSpan.FromMilliseconds(Math.Clamp((nextScan - DateTimeOffset.UtcNow).TotalMilliseconds, 1, 1000)), wait.Token);
                if (await Task.WhenAny(ready, delay) == ready) { await ready; ready = null; wait.Cancel(); }
            }
        }
        finally { subscription?.Dispose(); }

        List<SystemChange> Drain()
        {
            var pending = new List<SystemChange>();
            while (queue.Reader.TryRead(out var change))
            {
                if (change.Failure != null)
                {
                    Logger.LogWarning(change.Failure, "{Source} monitor failed; retrying in 30 seconds.", schema);
                    subscription?.Dispose(); subscription = null;
                    nextRetry = DateTimeOffset.UtcNow.AddSeconds(30);
                }
                else pending.Add(change);
            }
            return pending;
        }
        void Publish(IReadOnlyList<Records.DataRecord> records) { foreach (var record in records) Buffer.Add(record); }
    }
}

internal sealed class StorageEventsService(IOptions<SourcesConfiguration> config, IEventBuffer buffer, ILogger<StorageEventsService> logger)
    : SystemEventService<StorageEventsConfiguration>(config.Value.StorageEvents, buffer, "System.Storage.Events", logger,
        () => OperatingSystem.IsWindows() ? new WindowsStorageProvider() : OperatingSystem.IsLinux() ? new LinuxStorageProvider() : null);

internal sealed class NetworkEventsService(IOptions<SourcesConfiguration> config, IEventBuffer buffer, ILogger<NetworkEventsService> logger)
    : SystemEventService<NetworkEventsConfiguration>(config.Value.NetworkEvents, buffer, "System.Network.Events", logger,
        () => OperatingSystem.IsWindows() || OperatingSystem.IsLinux() ? new NetworkEventProvider() : null);
