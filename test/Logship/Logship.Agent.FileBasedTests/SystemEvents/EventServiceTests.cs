using System.Collections.Concurrent;
using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Logship.Agent.Core.Records;
using Logship.Agent.Core.Services;
using Logship.Agent.Core.Services.Sources.Common.SystemEvents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Logship.Agent.FileBasedTests.SystemEvents;

[TestClass]
public sealed class EventServiceTests
{
    [TestMethod]
    [Timeout(10000)]
    public async Task ScanFailureDoesNotDisconnectDevicesAndShutdownDisposesMonitor()
    {
        var buffer = new CaptureBuffer();
        var provider = new FakeProvider();
        var service = new TestService(buffer, provider);
        await service.StartAsync(CancellationToken.None);
        try
        {
            await Until(() => provider.Scans >= 3);
            Assert.AreEqual(0, buffer.Records.Count);
            provider.Remove();
            await Until(() => !buffer.Records.IsEmpty);
            Assert.AreEqual("DiskRemoved", buffer.Records.Single().Data["Action"]);
            Assert.AreEqual("Backup", buffer.Records.Single().Data["VolumeLabel"]);
        }
        finally { await service.StopAsync(CancellationToken.None); }
        Assert.IsTrue(provider.Disposed);
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task PowerEventRequestsPromptFlushButStoppingServiceDoesNotInventShutdown()
    {
        var buffer = new CaptureBuffer();
        var source = new FakePower();
        var sink = new FakeSink();
        var service = new PowerEventsService(Options.Create(new SourcesConfiguration { PowerEvents = new() { Enabled = true } }),
            buffer, sink, NullLogger<PowerEventsService>.Instance, () => [source]);
        await service.StartAsync(CancellationToken.None);
        try
        {
            await Until(() => source.Callback != null);
            source.Callback!(new("Suspending", DateTimeOffset.UtcNow));
            await Until(() => sink.Flushes == 1);
            Assert.AreEqual("Suspending", buffer.Records.Single().Data["Action"]);
        }
        finally { await service.StopAsync(CancellationToken.None); }
        Assert.AreEqual(1, buffer.Records.Count);
        Assert.IsTrue(source.Disposed);
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task AgentPushServiceFlushesOnceMoreOnStop()
    {
        var sink = new FakeSink();
        var service = new AgentPushService(Options.Create(new OutputConfiguration { Interval = TimeSpan.FromMilliseconds(100) }),
            sink, NullLogger<AgentPushService>.Instance);
        await service.StartAsync(CancellationToken.None);
        await Until(() => sink.Flushes >= 1);
        var previous = sink.Flushes;
        await service.StopAsync(CancellationToken.None);
        Assert.IsTrue(sink.Flushes > previous);
    }

    [TestMethod]
    public async Task DisabledPowerSourceNeverCreatesNativeSubscriptions()
    {
        var service = new PowerEventsService(Options.Create(new SourcesConfiguration { PowerEvents = new() }),
            new CaptureBuffer(), new FakeSink(), NullLogger<PowerEventsService>.Instance,
            () => throw new InvalidOperationException("Disabled collector ran."));
        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task FinalFlushHonorsHostShutdownCancellation()
    {
        var sink = new FakeSink();
        var service = new AgentPushService(Options.Create(new OutputConfiguration { Interval = TimeSpan.FromMilliseconds(100) }),
            sink, NullLogger<AgentPushService>.Instance);
        await service.StartAsync(CancellationToken.None);
        await Until(() => sink.Flushes >= 1);
        sink.OnFlush = token => Task.Delay(Timeout.Infinite, token);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await service.StopAsync(timeout.Token);
        Assert.IsTrue(timeout.IsCancellationRequested);
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task ConcurrentFlushRequestsSerializeAndCancellationRetainsUnsentBatches()
    {
        var buffer = new CaptureBuffer();
        buffer.Add(new DataRecord("Test", DateTimeOffset.UtcNow, new()));
        var output = new BlockingOutput();
        using var sink = new EventSink(Options.Create(new OutputConfiguration { MaximumFlushSize = 1 }), output, buffer, NullLogger<EventSink>.Instance);
        var first = sink.FlushAsync(CancellationToken.None);
        await output.Started.Task;
        buffer.Add(new DataRecord("Test", DateTimeOffset.UtcNow, new()));
        var second = sink.FlushAsync(CancellationToken.None);
        await Task.Delay(30);
        Assert.AreEqual(1, output.Calls);
        output.Release.TrySetResult();
        await Task.WhenAll(first, second);
        Assert.AreEqual(2, output.Calls);

        buffer.Add(new DataRecord("Test", DateTimeOffset.UtcNow, new()));
        buffer.Add(new DataRecord("Test", DateTimeOffset.UtcNow, new()));
        buffer.Add(new DataRecord("Test", DateTimeOffset.UtcNow, new()));
        using var timeout = new CancellationTokenSource();
        output.OnSend = () => { timeout.Cancel(); throw new OperationCanceledException(timeout.Token); };
        await sink.FlushAsync(timeout.Token);
        Assert.AreEqual(3, buffer.Records.Count);
    }

    internal static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private sealed class TestService(CaptureBuffer buffer, ISystemEventProvider provider)
        : SystemEventService<StorageEventsConfiguration>(new() { Enabled = true, Interval = TimeSpan.FromMilliseconds(30) },
            buffer, "System.Storage.Events", NullLogger<TestService>.Instance, () => provider);

    private sealed class FakeProvider : ISystemEventProvider
    {
        private int scans;
        private volatile bool removed;
        private Action<SystemChange>? callback;
        public int Scans => Volatile.Read(ref scans);
        public bool Disposed { get; private set; }
        public IReadOnlyList<SystemEntity> Snapshot()
        {
            var count = Interlocked.Increment(ref scans);
            if (count is 2 or 3) throw new IOException("Unavailable.");
            return removed ? [] : [LinuxStorageProvider.Device("sda", false, "Backup")];
        }
        public IDisposable Subscribe(Action<SystemChange> notify)
        {
            callback = notify;
            return new Subscription(() => { Disposed = true; callback = null; });
        }
        public void Remove() { removed = true; callback!(new(LinuxStorageProvider.Device("sda", false), false, DateTimeOffset.UtcNow)); }
    }
    private sealed class FakePower : IPowerEventSource
    {
        public Action<PowerNotice>? Callback;
        public bool Disposed;
        public IDisposable Subscribe(Action<PowerNotice> notify) { Callback = notify; return new Subscription(() => Disposed = true); }
    }
    private sealed class Subscription(Action dispose) : IDisposable { public void Dispose() => dispose(); }
    private sealed class FakeSink : IEventSink
    {
        private int flushes;
        public Func<CancellationToken, Task>? OnFlush { get; set; }
        public int Flushes => Volatile.Read(ref flushes);
        public Task FlushAsync(CancellationToken token) { Interlocked.Increment(ref flushes); return OnFlush?.Invoke(token) ?? Task.CompletedTask; }
        public void Add(DataRecord record) => throw new NotSupportedException();
        public void Add(IReadOnlyCollection<DataRecord> records) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<DataRecord>> NextAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<bool> SendAsync(IReadOnlyCollection<DataRecord> records, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class CaptureBuffer : IEventBuffer
    {
        public ConcurrentQueue<DataRecord> Records { get; } = new();
        public void Add(DataRecord record) => Records.Enqueue(record);
        public void Add(IReadOnlyCollection<DataRecord> records) { foreach (var record in records) Add(record); }
        public Task<IReadOnlyCollection<DataRecord>> NextAsync(CancellationToken token)
        {
            var records = new List<DataRecord>();
            while (Records.TryDequeue(out var record)) records.Add(record);
            return Task.FromResult<IReadOnlyCollection<DataRecord>>(records);
        }
    }
    private sealed class BlockingOutput : IEventOutput
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int calls;
        public int Calls => Volatile.Read(ref calls);
        public Action? OnSend { get; set; }
        public async Task<bool> SendAsync(IReadOnlyCollection<DataRecord> records, CancellationToken token)
        {
            Interlocked.Increment(ref calls);
            Started.TrySetResult();
            await Release.Task.WaitAsync(token);
            OnSend?.Invoke();
            return true;
        }
    }
}
