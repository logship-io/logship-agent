using System.Collections.Concurrent;
using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Logship.Agent.Core.Records;
using Logship.Agent.Core.Services.Sources.Common.Usb;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Logship.Agent.FileBasedTests.Usb;

[TestClass]
public sealed class UsbServiceTests
{
    [TestMethod]
    public async Task DisabledSourceDoesNotCreateProvider()
    {
        var service = new UsbService(Options.Create(new SourcesConfiguration { USB = new() }),
            new CaptureBuffer(), NullLogger<UsbService>.Instance, () => throw new InvalidOperationException("Disabled source ran."));
        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task FailedScansRetainStateAndMonitorRecoversAndDisposes()
    {
        var buffer = new CaptureBuffer();
        var provider = new FakeProvider();
        var service = new UsbService(Options.Create(new SourcesConfiguration
        {
            USB = new() { Enabled = true, Interval = TimeSpan.FromMilliseconds(30) },
        }), buffer, NullLogger<UsbService>.Instance, () => provider);
        await service.StartAsync(CancellationToken.None);
        try
        {
            await Until(() => provider.ScanCount >= 3 && provider.Subscribed);
            Assert.IsFalse(buffer.Records.Any(r => r.Schema == "System.USB.Events"));
            provider.Send(new UsbChange(new UsbDevice("camera"), false, DateTimeOffset.UtcNow));
            await Until(() => buffer.Records.Any(r => r.Schema == "System.USB.Events"));
            var removal = buffer.Records.First(r => r.Schema == "System.USB.Events");
            Assert.AreEqual("Disconnected", removal.Data["Action"]);
            Assert.AreEqual("cached", removal.Data["SerialNumber"]);
        }
        finally { await service.StopAsync(CancellationToken.None); }
        Assert.IsTrue(provider.Disposed);
        Assert.IsFalse(provider.Subscribed);
    }

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private sealed class FakeProvider : IUsbProvider
    {
        private int scans, attempts;
        private Action<UsbChange>? callback;
        private volatile bool removed;
        public int ScanCount => Volatile.Read(ref scans);
        public bool Subscribed => callback != null;
        public bool Disposed { get; private set; }
        public IReadOnlyList<UsbDevice> Enumerate()
        {
            var count = Interlocked.Increment(ref scans);
            if (count is 2 or 3) throw new IOException("Scan failed.");
            return removed ? [] : [new UsbDevice("camera", SerialNumber: "cached")];
        }
        public UsbDevice Read(string id) => new(id);
        public IDisposable Subscribe(Action<UsbChange> change, Action<Exception> failure)
        {
            if (Interlocked.Increment(ref attempts) == 1) throw new IOException("Monitor unavailable.");
            callback = change;
            return new Subscription(() => callback = null);
        }
        public void Send(UsbChange change) { removed = !change.Connected; callback!(change); }
        public void Dispose() => Disposed = true;
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    private sealed class CaptureBuffer : IEventBuffer
    {
        public ConcurrentQueue<DataRecord> Records { get; } = new();
        public void Add(DataRecord data) => Records.Enqueue(data);
        public void Add(IReadOnlyCollection<DataRecord> data) { foreach (var record in data) Add(record); }
        public Task<IReadOnlyCollection<DataRecord>> NextAsync(CancellationToken token) => throw new NotSupportedException();
    }
}
