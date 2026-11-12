using System.Globalization;
using System.Threading.Channels;
using System.Xml.Linq;
using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Logship.Agent.Core.Records;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Logship.Agent.Core.Services.Sources.Common.SystemEvents;

internal sealed record PowerNotice(string Action, DateTimeOffset Timestamp, string? Xml = null, Exception? Failure = null, int Source = 0);

internal interface IPowerEventSource
{
    IDisposable Subscribe(Action<PowerNotice> notify);
}

internal sealed class PowerState
{
    private bool sleeping;
    private DateTimeOffset lastShutdown = DateTimeOffset.MinValue;
    internal DataRecord? Apply(PowerNotice notice)
    {
        if (notice.Action == "Suspending") { if (sleeping) return null; sleeping = true; }
        if (notice.Action == "Resumed") { if (!sleeping) return null; sleeping = false; }
        if (notice.Action == "ShutdownRequested")
        {
            var elapsed = notice.Timestamp - lastShutdown;
            if (elapsed >= TimeSpan.Zero && elapsed < TimeSpan.FromSeconds(5)) return null;
            lastShutdown = notice.Timestamp;
        }
        var fields = new Dictionary<string, object> { ["machine"] = Environment.MachineName, ["Action"] = notice.Action, ["DetectionMethod"] = "Native" };
        if (notice.Xml != null) AddShutdownMetadata(notice.Xml, fields);
        return new("System.Power.Events", notice.Timestamp, fields);
    }

    internal static void AddShutdownMetadata(string xml, Dictionary<string, object> fields)
    {
        var document = XDocument.Parse(xml);
        XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
        var system = document.Root?.Element(ns + "System");
        var recordId = system?.Element(ns + "EventRecordID")?.Value;
        if (recordId != null) fields["EventRecordId"] = recordId;
        var original = system?.Element(ns + "TimeCreated")?.Attribute("SystemTime")?.Value;
        if (DateTimeOffset.TryParse(original, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)) fields["SourceTimestamp"] = time;
        var names = new Dictionary<string, string> { ["param1"] = "Process", ["param3"] = "Reason", ["param4"] = "ReasonCode", ["param5"] = "ShutdownType", ["param6"] = "Comment", ["param7"] = "User" };
        foreach (var element in document.Root?.Element(ns + "EventData")?.Elements(ns + "Data") ?? [])
        {
            var name = element.Attribute("Name")?.Value;
            if (name != null && names.TryGetValue(name, out var key)) fields[key] = element.Value;
        }
    }
}

internal sealed class PowerEventsService : BaseInputService<PowerEventsConfiguration>
{
    private readonly IEventSink sink;
    private readonly Func<IPowerEventSource[]> factory;
    public PowerEventsService(IOptions<SourcesConfiguration> config, IEventBuffer buffer, IEventSink sink, ILogger<PowerEventsService> logger)
        : this(config, buffer, sink, logger, () => OperatingSystem.IsWindows() ? [new WindowsSuspendEvents(), new WindowsShutdownEvents()]
            : OperatingSystem.IsLinux() ? [new LinuxPowerEvents()] : []) { }
    internal PowerEventsService(IOptions<SourcesConfiguration> config, IEventBuffer buffer, IEventSink sink,
        ILogger<PowerEventsService> logger, Func<IPowerEventSource[]> factory) : base(config.Value.PowerEvents, buffer, nameof(PowerEventsService), logger)
    { this.sink = sink; this.factory = factory; }

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var sources = factory();
        if (sources.Length == 0) { Logger.LogWarning("Power events are supported only on Windows and Linux."); return; }
        var queue = Channel.CreateUnbounded<PowerNotice>(new() { SingleReader = true });
        var subscriptions = new IDisposable?[sources.Length];
        var retry = new DateTimeOffset[sources.Length];
        var state = new PowerState();
        Task<bool>? ready = null;
        try
        {
            while (!token.IsCancellationRequested)
            {
                for (var i = 0; i < sources.Length; i++)
                {
                    if (subscriptions[i] != null || DateTimeOffset.UtcNow < retry[i]) continue;
                    var source = i;
                    try { subscriptions[i] = sources[i].Subscribe(n => queue.Writer.TryWrite(n with { Source = source })); }
                    catch (Exception ex) { Logger.LogWarning(ex, "Power event source {Source} unavailable; retrying in 30 seconds.", i); }
                    retry[i] = DateTimeOffset.UtcNow.AddSeconds(30);
                }
                while (queue.Reader.TryRead(out var notice))
                {
                    if (notice.Failure != null)
                    {
                        Logger.LogWarning(notice.Failure, "Power event source {Source} failed; retrying in 30 seconds.", notice.Source);
                        subscriptions[notice.Source]?.Dispose(); subscriptions[notice.Source] = null;
                        retry[notice.Source] = DateTimeOffset.UtcNow.AddSeconds(30);
                        continue;
                    }
                    try
                    {
                        var record = state.Apply(notice);
                        if (record == null) continue;
                        Buffer.Add(record);
                        if (notice.Action is "Suspending" or "ShutdownRequested")
                        {
                            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                            timeout.CancelAfter(TimeSpan.FromSeconds(1));
                            try { await sink.FlushAsync(timeout.Token); }
                            catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
                            catch (Exception ex) { Logger.LogWarning(ex, "Could not flush power event before suspension/shutdown."); }
                        }
                    }
                    catch (Exception ex) { Logger.LogWarning(ex, "Could not process power notification."); }
                }
                ready ??= queue.Reader.WaitToReadAsync(token).AsTask();
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
                var delay = Task.Delay(1000, wait.Token);
                if (await Task.WhenAny(ready, delay) == ready) { await ready; ready = null; wait.Cancel(); }
            }
        }
        finally { foreach (var subscription in subscriptions) subscription?.Dispose(); }
    }
}
