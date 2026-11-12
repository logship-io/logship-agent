using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Services.Sources.Common.Usb;
using Logship.Agent.Core.Records;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace Logship.Agent.FileBasedTests.Usb;

[TestClass]
public sealed class UsbTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly UsbDevice Camera = new("usb-camera", "1234", "abcd", "Vendor", "Camera", "serial", "port-1");

    [TestMethod]
    public void BaselineAndUnchangedScanOnlyPublishDevices()
    {
        var state = new UsbState();
        var records = state.Reconcile([Camera], Now, [new(Camera, true, Now)]);
        Assert.AreEqual(1, records.Count);
        Assert.AreEqual("System.USB.Devices", records[0].Schema);
        Assert.AreEqual(1, state.Inventory([Camera], Now).Count());
        Assert.IsFalse(records[0].Data.ContainsKey("SnapshotId"));
        var json = JsonSerializer.Serialize(records[0], RecordSourceGenerationContext.Default.DataRecord);
        StringAssert.Contains(json, "abcd");
    }

    [TestMethod]
    public void BriefConnectionAndDuplicateEventsRetainRemovalMetadata()
    {
        var state = new UsbState();
        Assert.AreEqual(0, state.Inventory([], Now).Count());
        Assert.IsNotNull(state.Change(new(Camera, true, Now)));
        Assert.IsNull(state.Change(new(new UsbDevice(Camera.DeviceId), true, Now)));
        var removal = state.Change(new(new UsbDevice(Camera.DeviceId), false, Now));
        Assert.IsNotNull(removal);
        Assert.AreEqual("Disconnected", removal.Data["Action"]);
        Assert.AreEqual("serial", removal.Data["SerialNumber"]);
        Assert.IsNull(state.Change(new(Camera, false, Now)));
        Assert.AreEqual(0, state.Inventory([], Now).Count());
    }

    [TestMethod]
    public void ReconciliationTracksIdenticalDevicesByAttachmentIdentity()
    {
        var state = new UsbState();
        _ = state.Inventory([Camera], Now).ToArray();
        var second = Camera with { DeviceId = "usb-camera-2" };
        var records = state.Inventory([second], Now).ToArray();
        Assert.AreEqual(3, records.Length);
        Assert.AreEqual("Connected", records[0].Data["Action"]);
        Assert.AreEqual("Disconnected", records[1].Data["Action"]);
        Assert.AreEqual("Reconciliation", records[1].Data["DetectionMethod"]);
    }

    [TestMethod]
    public void NativeEventsDuringScanOverrideStaleSnapshot()
    {
        var state = new UsbState();
        _ = state.Inventory([Camera], Now).ToArray();
        var records = state.Reconcile([Camera], Now, [new(Camera, false, Now)]);
        Assert.AreEqual(1, records.Count);
        Assert.AreEqual("Disconnected", records[0].Data["Action"]);
        Assert.AreEqual(0, state.Inventory([], Now).Count());

        records = state.Reconcile([], Now, [new(Camera, true, Now), new(Camera, false, Now)]);
        Assert.AreEqual(2, records.Count);
        Assert.IsTrue(records.All(r => r.Schema == "System.USB.Events"));
    }

    [TestMethod]
    public void FiltersExcludeRootHubsAndCompositeInterfaces()
    {
        Assert.IsTrue(WindowsUsbProvider.IsDevice(@"USB\VID_1234&PID_ABCD\serial"));
        Assert.IsFalse(WindowsUsbProvider.IsDevice(@"USB\VID_1234&PID_ABCD&MI_00\serial"));
        Assert.IsFalse(WindowsUsbProvider.IsDevice(@"USB\ROOT_HUB30\1"));
        Assert.IsTrue(LinuxUsbProvider.IsDeviceName("1-2.3"));
        Assert.IsFalse(LinuxUsbProvider.IsDeviceName("1-2:1.0"));
        Assert.IsFalse(LinuxUsbProvider.IsDeviceName("usb1"));
        Assert.AreEqual("00ab", UsbDevice.HexId("AB"));
        Assert.IsNull(UsbDevice.HexId("invalid"));
    }

    [TestMethod]
    public void ConfigurationIsOptInAndMetadataIsOptional()
    {
        var config = JsonSerializer.Deserialize("{\"USB\":{\"enabled\":true,\"interval\":\"00:00:05\"}}", SourceGenerationContext.Default.SourcesConfiguration);
        Assert.IsNotNull(config?.USB);
        Assert.IsTrue(config.USB.Enabled);
        Assert.AreEqual(TimeSpan.FromSeconds(5), config.USB.Interval);
        Assert.IsFalse(new UsbConfiguration().Enabled);
        Assert.AreEqual(TimeSpan.FromSeconds(30), new UsbConfiguration().Interval);
        var data = new UsbDevice("minimal").Record("System.USB.Devices", Now).Data;
        Assert.AreEqual(2, data.Count);
    }

    [TestMethod]
    public void NativeWindowsEnumerationAndSubscriptionSmokeTest()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("Requires Windows."); return; }
        using var provider = new WindowsUsbProvider();
        using var subscription = provider.Subscribe(_ => { }, _ => { });
        var devices = provider.Enumerate();
        Assert.IsTrue(devices.All(d => WindowsUsbProvider.IsDevice(d.DeviceId)));
        Assert.AreEqual(devices.Count, devices.Select(d => d.DeviceId).Distinct().Count());
    }
}
