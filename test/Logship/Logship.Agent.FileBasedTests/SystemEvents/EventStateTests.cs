using System.Buffers.Binary;
using System.Net;
using System.Text.Json;
using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Configuration.Validators;
using Logship.Agent.Core.Records;
using Logship.Agent.Core.Services.Sources.Common.SystemEvents;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Logship.Agent.FileBasedTests.SystemEvents;

[TestClass]
public sealed class EventStateTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly SystemEntity Disk = LinuxStorageProvider.Device("sda", false, "Backup", "ext4");

    [TestMethod]
    public void BaselineAndDuplicateNotificationsAreSilentAndRemovalUsesCache()
    {
        var state = new EventState("System.Storage.Events");
        Assert.AreEqual(0, state.Reconcile([Disk], [new(Disk, true, Now)], "Native", Now).Count);
        Assert.AreEqual(0, state.Apply(new(LinuxStorageProvider.Device("sda", false), true, Now)).Count);
        var removed = state.Apply(new(LinuxStorageProvider.Device("sda", false), false, Now));
        Assert.AreEqual(1, removed.Count);
        Assert.AreEqual("DiskRemoved", removed[0].Data["Action"]);
        Assert.AreEqual("Backup", removed[0].Data["VolumeLabel"]);
        Assert.AreEqual(0, state.Apply(new(Disk, false, Now)).Count);
        Assert.AreEqual(0, state.Reconcile([], [], "Reconciliation", Now).Count);
    }

    [TestMethod]
    public void NativeChangesDuringScanOverrideStaleSnapshotAndCaptureBriefDevices()
    {
        var state = new EventState("System.Storage.Events");
        _ = state.Reconcile([Disk], [], "Reconciliation", Now);
        var records = state.Reconcile([Disk], [new(Disk, false, Now)], "Reconciliation", Now);
        Assert.AreEqual(1, records.Count);
        records = state.Reconcile([], [new(Disk, true, Now), new(Disk, false, Now)], "Reconciliation", Now);
        CollectionAssert.AreEqual(new[] { "DiskAdded", "DiskRemoved" }, records.Select(r => (string)r.Data["Action"]).ToArray());
        Assert.IsTrue(records.All(r => (string)r.Data["DetectionMethod"] == "Native"));
    }

    [TestMethod]
    public void RouteMetricChangeRemovesOldRouteThenAddsReplacement()
    {
        var state = new EventState("System.Network.Events");
        var route = NetworkEventProvider.Route(7, IPAddress.Any, 0, IPAddress.Parse("192.0.2.1"), 10);
        _ = state.Reconcile([route], [], "Reconciliation", Now);
        var changed = NetworkEventProvider.Route(7, IPAddress.Any, 0, IPAddress.Parse("192.0.2.1"), 20);
        var records = state.Reconcile([changed], [], "Reconciliation", Now);
        Assert.AreEqual(2, records.Count);
        Assert.AreEqual("RouteRemoved", records[0].Data["Action"]);
        Assert.AreEqual(10u, records[0].Data["Metric"]);
        Assert.AreEqual("RouteAdded", records[1].Data["Action"]);
        Assert.AreEqual(20u, records[1].Data["Metric"]);
        Assert.AreEqual(2, state.Apply(new(route, true, Now)).Count);
    }

    [TestMethod]
    public void MountParserHandlesEscapesBindMountsAndRejectsPartialInput()
    {
        var mounts = LinuxStorageProvider.ParseMounts([
            @"36 25 8:1 / /media/My\040Disk rw - ext4 /dev/sda1 rw",
            @"37 25 8:1 /subdir /srv/bind rw - ext4 /dev/sda1 rw",
            @"38 25 0:4 / /remote rw - nfs server:/export rw"]);
        Assert.AreEqual(3, mounts.Count);
        Assert.AreEqual("/media/My Disk", mounts[0].Fields["MountLocation"]);
        Assert.AreEqual("/subdir", mounts[1].Fields["MountRoot"]);
        Assert.AreEqual("nfs", mounts[2].Fields["FileSystem"]);
        Assert.AreEqual(@"\040", LinuxStorageProvider.Unescape(@"\134040"));
        Assert.ThrowsExactly<IOException>(() => LinuxStorageProvider.ParseMounts(["broken"]));
    }

    [TestMethod]
    public void NetlinkParsersHandleAddressesRoutesAndInvalidMessages()
    {
        var address = new byte[16];
        address[0] = 2; address[1] = 24;
        BinaryPrimitives.WriteInt32LittleEndian(address.AsSpan(4), 7);
        Attribute(address.AsSpan(8), 2, IPAddress.Parse("192.0.2.10").GetAddressBytes());
        var entity = LinuxNetworkEvents.ParseAddress(address);
        Assert.IsNotNull(entity);
        Assert.AreEqual("192.0.2.10", entity.Fields["Address"]);
        Assert.AreEqual(24, entity.Fields["PrefixLength"]);

        var route = new byte[36];
        route[0] = 2; route[4] = 254; route[7] = 1;
        Attribute(route.AsSpan(12), 4, BitConverter.GetBytes(7));
        Attribute(route.AsSpan(20), 5, IPAddress.Parse("192.0.2.1").GetAddressBytes());
        Attribute(route.AsSpan(28), 6, BitConverter.GetBytes(100));
        entity = LinuxNetworkEvents.ParseRoutes(route).Single();
        Assert.AreEqual("0.0.0.0", entity.Fields["Destination"]);
        Assert.AreEqual(100u, entity.Fields["Metric"]);
        Assert.AreEqual(254u, entity.Fields["Table"]);
        Assert.ThrowsExactly<IOException>(() => LinuxNetworkEvents.ParseMessages(new byte[16], (_, _) => { }));
        var interrupted = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(interrupted, 16);
        interrupted[6] = 0x10;
        Assert.ThrowsExactly<IOException>(() => LinuxNetworkEvents.ParseMessages(interrupted, (_, _) => { }));
    }

    private static void Attribute(Span<byte> output, ushort type, byte[] value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(output, (ushort)(value.Length + 4));
        BinaryPrimitives.WriteUInt16LittleEndian(output[2..], type);
        value.CopyTo(output[4..]);
    }

    [TestMethod]
    public void ConfigurationBindsAndValidatesAndRecordsSerializeWithGeneratedContext()
    {
        var config = JsonSerializer.Deserialize("{\"StorageEvents\":{\"enabled\":true,\"interval\":\"00:00:05\"},\"NetworkEvents\":{\"enabled\":true},\"PowerEvents\":{\"enabled\":true}}", SourceGenerationContext.Default.SourcesConfiguration);
        Assert.IsNotNull(config);
        Assert.IsTrue(config.StorageEvents!.Enabled);
        Assert.IsTrue(config.NetworkEvents!.Enabled);
        Assert.IsTrue(config.PowerEvents!.Enabled);
        Assert.IsFalse(new PowerEventsConfiguration().Enabled);
        Assert.IsFalse(new NetworkEventsConfiguration().Enabled);
        Assert.IsFalse(new StorageEventsConfiguration().Enabled);
        config.StorageEvents.Interval = TimeSpan.Zero;
        Assert.IsTrue(new SourcesConfigurationValidator().Validate(null, config).Failed);
        var record = NetworkEventProvider.Route(1, IPAddress.IPv6Any, 0, IPAddress.Parse("fe80::1"), 10).Record("System.Network.Events", true, "Native", Now);
        var json = JsonSerializer.Serialize(record, RecordSourceGenerationContext.Default.DataRecord);
        StringAssert.Contains(json, "RouteAdded");
        Assert.IsFalse(record.Data.ContainsKey("SnapshotId"));
    }

    [TestMethod]
    public void PowerEventsDeduplicateResumeAndShutdownAndPreserveMetadata()
    {
        var state = new PowerState();
        Assert.IsNull(state.Apply(new("Resumed", Now)));
        Assert.IsNotNull(state.Apply(new("Suspending", Now)));
        Assert.IsNull(state.Apply(new("Suspending", Now)));
        Assert.IsNotNull(state.Apply(new("Resumed", Now.AddSeconds(1))));
        Assert.IsNull(state.Apply(new("Resumed", Now.AddSeconds(2))));
        const string xml = "<Event xmlns=\"http://schemas.microsoft.com/win/2004/08/events/event\"><System><EventRecordID>123</EventRecordID></System><EventData><Data Name=\"param1\">shutdown.exe</Data><Data Name=\"param5\">restart</Data></EventData></Event>";
        var record = state.Apply(new("ShutdownRequested", Now, xml));
        Assert.IsNotNull(record);
        Assert.AreEqual("123", record.Data["EventRecordId"]);
        Assert.AreEqual("restart", record.Data["ShutdownType"]);
        Assert.IsNull(state.Apply(new("ShutdownRequested", Now.AddSeconds(1), xml)));
    }

    [TestMethod]
    public void WindowsNativeSubscriptionsAndInventoriesSmokeTest()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("Requires Windows."); return; }
        var storage = new WindowsStorageProvider();
        using var storageMonitor = storage.Subscribe(_ => { });
        var disks = storage.Snapshot();
        Assert.AreEqual(disks.Count, disks.Select(e => e.Key).Distinct().Count());
        var network = new NetworkEventProvider();
        using var networkMonitor = network.Subscribe(_ => { });
        var entities = network.Snapshot();
        Assert.IsTrue(entities.Any(e => e.AddedAction == "InterfaceAdded"));
        Assert.IsTrue(entities.Any(e => e.AddedAction == "RouteAdded"));
        using var power = new WindowsSuspendEvents().Subscribe(_ => { });
        using var shutdown = new WindowsShutdownEvents().Subscribe(_ => { });
    }

    [TestMethod]
    public void LinuxNativeStorageAndNetworkSmokeTest()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Inconclusive("Requires Linux."); return; }
        var storage = new LinuxStorageProvider();
        using var storageMonitor = storage.Subscribe(_ => { });
        var devices = storage.Snapshot();
        Assert.IsTrue(devices.Any(e => e.AddedAction == "Mounted"));
        Assert.AreEqual(devices.Count, devices.Select(e => e.Key).Distinct().Count());
        var network = new NetworkEventProvider();
        using var networkMonitor = network.Subscribe(_ => { });
        var entities = network.Snapshot();
        Assert.IsTrue(entities.Any(e => e.AddedAction == "InterfaceAdded"));
        Assert.IsTrue(entities.Any(e => e.AddedAction == "RouteAdded"));
        Assert.AreEqual(entities.Count, entities.Select(e => e.Key).Distinct().Count());
    }
}
