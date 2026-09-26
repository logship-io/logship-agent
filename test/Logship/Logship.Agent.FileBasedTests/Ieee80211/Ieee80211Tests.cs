using System.Buffers.Binary;
using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Logship.Agent.Core.Records;
using Logship.Agent.Core.Services.Sources.Linux.Ieee80211;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Logship.Agent.FileBasedTests.Ieee80211;

[TestClass]
public sealed class Ieee80211Tests
{
    [TestMethod]
    [DataRow(4, "ProbeRequest")]
    [DataRow(5, "ProbeResponse")]
    [DataRow(8, "Beacon")]
    public void ParsesManagementFrames(int subtype, string type)
    {
        var frame = Frame(subtype, [0, 3, 65, 66, 67, 3, 1, 11]);
        var result = Ieee80211FrameParser.Parse(frame, 105);
        Assert.IsNotNull(result);
        Assert.AreEqual(type, result["FrameType"]);
        Assert.AreEqual("ABC", result["Ssid"]);
        Assert.AreEqual("414243", result["SsidHex"]);
        Assert.AreEqual(11, result["AdvertisedChannel"]);
        Assert.AreEqual("0a:0b:0c:0d:0e:0f", result["SourceMac"]);
        Assert.AreEqual(123, result["SequenceNumber"]);
        Assert.AreEqual(true, result["Retry"]);
        if (subtype != 4) Assert.AreEqual(true, result["Privacy"]);
    }

    [TestMethod]
    [DataRow(4, "WildcardProbe")]
    [DataRow(8, "HiddenSsid")]
    public void DistinguishesEmptyMissingAndBinarySsids(int subtype, string flag)
    {
        var empty = Ieee80211FrameParser.Parse(Frame(subtype, [0, 0]), 105)!;
        Assert.AreEqual(true, empty[flag]);
        var missing = Ieee80211FrameParser.Parse(Frame(subtype, []), 105)!;
        Assert.IsFalse(missing.ContainsKey("Ssid"));
        var binary = Ieee80211FrameParser.Parse(Frame(subtype, [0, 2, 255, 0]), 105)!;
        Assert.AreEqual("FF00", binary["SsidHex"]);
    }

    [TestMethod]
    [DataRow(2412, 1, "2.4GHz")]
    [DataRow(5180, 36, "5GHz")]
    public void RadiotapHandlesExtendedBitmapAlignmentAndFcs(int frequency, int channel, string band)
    {
        byte[] packet = Radiotap(Frame(8, [0, 0]), frequency);
        var result = Ieee80211FrameParser.Parse(packet, 127)!;
        Assert.AreEqual(frequency, result["FrequencyMHz"]);
        Assert.AreEqual(channel, result["Channel"]);
        Assert.AreEqual(band, result["Band"]);
        Assert.AreEqual(-42, result["SignalDbm"]);
        Assert.AreEqual(38, result["FrameLength"]);
        packet[24] |= 0x40;
        Assert.IsNull(Ieee80211FrameParser.Parse(packet, 127));
    }

    [TestMethod]
    public void RejectsTruncationMalformedElementsAndUnrelatedFrames()
    {
        var valid = Radiotap(Frame(8, [0, 2, 65, 66]), 2412);
        for (int i = 0; i < valid.Length; i++)
            _ = Ieee80211FrameParser.Parse(valid.AsSpan(0, i), 127);
        Assert.IsNull(Ieee80211FrameParser.Parse(Frame(8, [0, 3, 65]), 105));
        Assert.IsNull(Ieee80211FrameParser.Parse(Frame(8, [0]), 105));
        Assert.IsNull(Ieee80211FrameParser.Parse(Frame(0, []), 105));
        Assert.IsNull(Ieee80211FrameParser.Parse(Frame(8, []), 1));
        byte[] dataFrame = Frame(8, []); dataFrame[0] |= 8;
        Assert.IsNull(Ieee80211FrameParser.Parse(dataFrame, 105));
        var noMetadata = new byte[8].Concat(Frame(4, [])).ToArray();
        noMetadata[2] = 8;
        Assert.IsFalse(Ieee80211FrameParser.Parse(noMetadata, 127)!.ContainsKey("FrequencyMHz"));
    }

    [TestMethod]
    public void FrequencyDiscoveryRespectsDisabledBandsAndKeepsPassiveChannels()
    {
        string info = " * 2412 MHz [1] (20.0 dBm)\n * 2437 MHz [6] (disabled)\n * 5180 MHz [36] (no IR)\n * 5260 MHz [52] (radar detection)\n * 5955 MHz [1] (20 dBm)\n";
        CollectionAssert.AreEqual(new[] { 2412, 5180, 5260 }, Ieee80211Adapter.ParseFrequencies(info).ToArray());
        Assert.AreEqual(14, Ieee80211FrameParser.FrequencyToChannel(2484));
    }

    [TestMethod]
    public async Task AdapterRestoresAfterPartialSetupFailure()
    {
        var calls = new List<string>();
        var adapter = new Ieee80211Adapter("wlan1", (tool, args, _) =>
        {
            string call = tool + " " + string.Join(" ", args); calls.Add(call);
            if (call == "iw dev wlan1 info") return Task.FromResult(" wiphy 1\n type managed\n");
            if (call == "ip -o link show dev wlan1") return Task.FromResult("2: wlan1: <BROADCAST,UP> state DOWN");
            if (call == "iw phy phy1 info") return Task.FromResult(" * monitor\n * 2412 MHz [1]\n");
            if (call.EndsWith("type monitor", StringComparison.Ordinal)) throw new IOException("setup failed");
            return Task.FromResult("");
        });
        await Assert.ThrowsExactlyAsync<IOException>(() => adapter.PrepareAsync([], CancellationToken.None));
        await adapter.RestoreAsync(CancellationToken.None);
        CollectionAssert.Contains(calls, "iw dev wlan1 set type managed");
        Assert.AreEqual("ip link set dev wlan1 up", calls[^1]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SessionCapturesAndHopsDuringIdleOrBusyTraffic(bool busy)
    {
        var adapter = new FakeAdapter();
        var capture = new FakeCapture(busy);
        var buffer = new TestBuffer();
        var config = new LinuxIeee80211Configuration { Interface = "wlan1", Schema = "Custom.80211", ChannelDwell = TimeSpan.FromMilliseconds(5) };
        var service = new LinuxIeee80211Service(config, buffer, NullLogger.Instance, () => adapter, _ => capture);
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        try { await service.RunSessionAsync(stop.Token); } catch (OperationCanceledException) { }
        Assert.IsTrue(adapter.Tunes > 1);
        Assert.IsTrue(adapter.Restored);
        Assert.IsTrue(capture.Disposed);
        Assert.IsTrue(buffer.Records.Count > 0);
        Assert.AreEqual("Custom.80211", buffer.Records[0].Schema);
        Assert.AreEqual(FakeCapture.Timestamp, buffer.Records[0].TimeStamp);
    }

    [TestMethod]
    public async Task FailedCaptureOpenStillRestoresAdapter()
    {
        var adapter = new FakeAdapter();
        var service = new LinuxIeee80211Service(new() { Interface = "wlan1" }, new TestBuffer(), NullLogger.Instance,
            () => adapter, _ => throw new IOException("missing libpcap"));
        await Assert.ThrowsExactlyAsync<IOException>(() => service.RunSessionAsync(CancellationToken.None));
        Assert.IsTrue(adapter.Restored);
    }

    [TestMethod]
    public async Task NonLinuxServiceDoesNotOpenAdapter()
    {
        if (OperatingSystem.IsLinux()) return;
        var service = new LinuxIeee80211Service(Options.Create(new SourcesConfiguration { LinuxIeee80211 = new() { Interface = "missing" } }),
            new TestBuffer(), NullLogger<LinuxIeee80211Service>.Instance);
        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);
    }

    private static byte[] Frame(int subtype, byte[] elements)
    {
        var frame = new byte[(subtype == 4 ? 24 : 36) + elements.Length];
        frame[0] = (byte)(subtype << 4); frame[1] = 8;
        for (int i = 0; i < 6; i++) frame[10 + i] = (byte)(10 + i);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(22), 123 << 4);
        if (subtype != 4) { frame[32] = 100; frame[34] = 16; }
        elements.CopyTo(frame, frame.Length - elements.Length);
        return frame;
    }

    private static byte[] Radiotap(byte[] frame, int frequency)
    {
        var packet = new byte[31 + frame.Length + 4];
        packet[2] = 31;
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), 0x8000002b); // TSFT, flags, channel, signal, extension
        packet[24] = 0x10;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(26), (ushort)frequency);
        packet[30] = unchecked((byte)-42);
        frame.CopyTo(packet, 31);
        return packet;
    }

    private sealed class FakeAdapter : IIeee80211Adapter
    {
        internal int Tunes;
        internal bool Restored;
        public Task<IReadOnlyList<int>> PrepareAsync(IReadOnlyCollection<int> requested, CancellationToken token) => Task.FromResult<IReadOnlyList<int>>([2412, 5180]);
        public Task TuneAsync(int frequency, CancellationToken token) { Tunes++; return Task.CompletedTask; }
        public Task RestoreAsync(CancellationToken token) { Restored = true; return Task.CompletedTask; }
    }

    private sealed class FakeCapture(bool busy) : IIeee80211Capture
    {
        internal static readonly DateTimeOffset Timestamp = DateTimeOffset.FromUnixTimeSeconds(123456789);
        private int reads;
        internal bool Disposed;
        public int LinkType => 105;
        public uint Dropped => 0;
        public (byte[] Packet, DateTimeOffset Timestamp)? Read() => busy || reads++ == 0 ? (Frame(4, [0, 0]), Timestamp) : null;
        public void Dispose() => Disposed = true;
    }

    private sealed class TestBuffer : IEventBuffer
    {
        internal List<DataRecord> Records { get; } = [];
        public void Add(DataRecord data) { if (Records.Count < 100) Records.Add(data); }
        public void Add(IReadOnlyCollection<DataRecord> data) => Records.AddRange(data);
        public Task<IReadOnlyCollection<DataRecord>> NextAsync(CancellationToken token) => Task.FromResult<IReadOnlyCollection<DataRecord>>(Records);
    }
}
