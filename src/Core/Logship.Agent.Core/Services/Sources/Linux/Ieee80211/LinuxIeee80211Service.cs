using System.Diagnostics.Metrics;
using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Logship.Agent.Core.Records;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Logship.Agent.Core.Services.Sources.Linux.Ieee80211;

internal sealed class LinuxIeee80211Service : BaseInputService<LinuxIeee80211Configuration>
{
    private static readonly Meter Meter = new("Logship.Agent.80211", "1.0.0");
    private static readonly Counter<long> CaptureDrops = Meter.CreateCounter<long>("logship.agent.80211.capture_drops");
    private static readonly Counter<long> InvalidFrames = Meter.CreateCounter<long>("logship.agent.80211.invalid_frames");
    private static readonly Counter<long> Failures = Meter.CreateCounter<long>("logship.agent.80211.capture_failures");
    private static readonly Counter<long> HopFailures = Meter.CreateCounter<long>("logship.agent.80211.hop_failures");
    private readonly Func<IIeee80211Adapter> adapterFactory;
    private readonly Func<string, IIeee80211Capture> captureFactory;

    public LinuxIeee80211Service(IOptions<SourcesConfiguration> config, IEventBuffer buffer, ILogger<LinuxIeee80211Service> logger)
        : this(config.Value.LinuxIeee80211, buffer, logger, null, null)
    {
        if (!OperatingSystem.IsLinux()) this.Enabled = false;
    }

    internal LinuxIeee80211Service(LinuxIeee80211Configuration? config, IEventBuffer buffer, ILogger logger,
        Func<IIeee80211Adapter>? adapterFactory, Func<string, IIeee80211Capture>? captureFactory)
        : base(config, buffer, nameof(LinuxIeee80211Service), logger)
    {
        this.adapterFactory = adapterFactory ?? (() => new Ieee80211Adapter(Config.Interface));
        this.captureFactory = captureFactory ?? (device => new PcapCapture(device));
    }

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try { await RunSessionAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                Failures.Add(1);
                Ieee80211Log.SessionFailed(Logger, Config.Interface, ex);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(30), token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        }
    }

    internal async Task RunSessionAsync(CancellationToken token)
    {
        var adapter = adapterFactory();
        using var session = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task? hopping = null;
        IIeee80211Capture? capture = null;
        try
        {
            var frequencies = await adapter.PrepareAsync(Config.FrequenciesMHz, token);
            // Tune successfully before opening capture, then revisit every frequency each sweep.
            int first = -1;
            for (int i = 0; i < frequencies.Count; i++)
            {
                try { await adapter.TuneAsync(frequencies[i], token); first = i; break; }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    HopFailures.Add(1);
                    Ieee80211Log.HopFailed(Logger, frequencies[i], ex);
                }
            }
            if (first < 0) throw new IOException("No frequency could be tuned.");
            capture = captureFactory(Config.Interface);
            hopping = HopAsync(adapter, frequencies, first, session.Token);
            uint previousDrops = 0;
            var nextStats = DateTimeOffset.UtcNow.AddSeconds(5);
            while (!session.IsCancellationRequested)
            {
                if (hopping.IsCompleted) await hopping;
                var observation = capture.Read();
                if (observation.HasValue)
                {
                    var data = Ieee80211FrameParser.Parse(observation.Value.Packet, capture.LinkType);
                    if (data == null) InvalidFrames.Add(1);
                    else
                    {
                        data["machine"] = Environment.MachineName;
                        data["Interface"] = Config.Interface;
                        Buffer.Add(new DataRecord(Config.Schema, observation.Value.Timestamp, data));
                    }
                }
                if (DateTimeOffset.UtcNow >= nextStats)
                {
                    uint drops = capture.Dropped;
                    CaptureDrops.Add(unchecked(drops - previousDrops));
                    previousDrops = drops;
                    nextStats = DateTimeOffset.UtcNow.AddSeconds(5);
                }
                // Yield even under sustained traffic so hopping and cancellation remain responsive.
                if (!observation.HasValue) await Task.Delay(10, session.Token);
                else await Task.Yield();
            }
            token.ThrowIfCancellationRequested();
        }
        finally
        {
            await session.CancelAsync();
            if (hopping != null)
            {
                try { await hopping; }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Ieee80211Log.SessionFailed(Logger, Config.Interface, ex); }
            }
            capture?.Dispose();
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { await adapter.RestoreAsync(cleanup.Token); }
            catch (Exception ex) { Ieee80211Log.RestoreFailed(Logger, Config.Interface, ex); }
        }
    }

    private async Task HopAsync(IIeee80211Adapter adapter, IReadOnlyList<int> frequencies, int first, CancellationToken token)
    {
        await Task.Delay(Config.ChannelDwell, token);
        while (true)
        {
            int successful = 0;
            for (int i = 1; i <= frequencies.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                int frequency = frequencies[(first + i) % frequencies.Count];
                try { await adapter.TuneAsync(frequency, token); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    HopFailures.Add(1);
                    Ieee80211Log.HopFailed(Logger, frequency, ex);
                    continue;
                }
                successful++;
                await Task.Delay(Config.ChannelDwell, token);
            }
            if (successful == 0) throw new IOException("No frequency could be tuned during sweep.");
        }
    }
}

internal static partial class Ieee80211Log
{
    [LoggerMessage(LogLevel.Error, "IEEE 802.11 capture failed on {Interface}; retrying in 30 seconds.")]
    internal static partial void SessionFailed(ILogger logger, string @interface, Exception exception);
    [LoggerMessage(LogLevel.Warning, "IEEE 802.11 frequency {Frequency} could not be tuned.")]
    internal static partial void HopFailed(ILogger logger, int frequency, Exception exception);
    [LoggerMessage(LogLevel.Error, "IEEE 802.11 adapter {Interface} could not be fully restored.")]
    internal static partial void RestoreFailed(ILogger logger, string @interface, Exception exception);
}
