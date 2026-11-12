using Logship.Agent.Core.Records;

namespace Logship.Agent.Core.Services.Sources.Common.Usb;

internal sealed record UsbDevice(string DeviceId, string? VendorId = null, string? ProductId = null,
    string? Manufacturer = null, string? Product = null, string? SerialNumber = null, string? Location = null)
{
    internal DataRecord Record(string schema, DateTimeOffset timestamp, string? action = null, string? method = null)
    {
        var data = new Dictionary<string, object> { ["machine"] = Environment.MachineName, ["DeviceId"] = DeviceId };
        void Add(string key, string? value) { if (!string.IsNullOrEmpty(value)) data[key] = value; }
        Add("VendorId", VendorId); Add("ProductId", ProductId); Add("Manufacturer", Manufacturer);
        Add("Product", Product); Add("SerialNumber", SerialNumber); Add("Location", Location);
        Add("Action", action); Add("DetectionMethod", method);
        return new DataRecord(schema, timestamp, data);
    }

    internal static string? HexId(string? value) => ushort.TryParse(value,
        System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var id)
        ? id.ToString("x4", System.Globalization.CultureInfo.InvariantCulture) : null;
}

internal sealed record UsbChange(UsbDevice Device, bool Connected, DateTimeOffset Timestamp);

internal interface IUsbProvider : IDisposable
{
    IReadOnlyList<UsbDevice> Enumerate();
    UsbDevice Read(string id);
    // Registration is synchronous: notifications must be active before enumeration begins.
    IDisposable Subscribe(Action<UsbChange> change, Action<Exception> failure);
}

internal sealed class UsbState
{
    private Dictionary<string, UsbDevice> devices = new(StringComparer.OrdinalIgnoreCase);
    private bool initialized;

    internal IReadOnlyList<DataRecord> Reconcile(IReadOnlyList<UsbDevice> snapshot, DateTimeOffset timestamp, IReadOnlyList<UsbChange> pending)
    {
        if (!initialized)
        {
            var baseline = Inventory(snapshot, timestamp).ToList();
            foreach (var change in pending)
            {
                var record = Change(change);
                if (record != null) baseline.Add(record);
            }
            return baseline;
        }
        var records = new List<DataRecord>();
        var adjusted = snapshot.ToDictionary(d => d.DeviceId, StringComparer.OrdinalIgnoreCase);
        foreach (var change in pending)
        {
            var record = Change(change);
            if (record != null) records.Add(record);
            // Native events observed during enumeration take precedence over the scan.
            if (devices.TryGetValue(change.Device.DeviceId, out var current)) adjusted[current.DeviceId] = current;
            else adjusted.Remove(change.Device.DeviceId);
        }
        records.AddRange(Inventory(adjusted.Values.ToArray(), timestamp));
        return records;
    }

    internal IEnumerable<DataRecord> Inventory(IReadOnlyList<UsbDevice> snapshot, DateTimeOffset timestamp)
    {
        var next = snapshot.ToDictionary(d => d.DeviceId, StringComparer.OrdinalIgnoreCase);
        var records = new List<DataRecord>();
        if (initialized)
        {
            foreach (var device in next.Values.Where(d => !devices.ContainsKey(d.DeviceId)))
                records.Add(device.Record("System.USB.Events", timestamp, "Connected", "Reconciliation"));
            foreach (var device in devices.Values.Where(d => !next.ContainsKey(d.DeviceId)))
                records.Add(device.Record("System.USB.Events", timestamp, "Disconnected", "Reconciliation"));
        }
        devices = next;
        initialized = true;
        records.AddRange(next.Values.Select(d => d.Record("System.USB.Devices", timestamp)));
        return records;
    }

    internal DataRecord? Change(UsbChange change)
    {
        if (change.Connected)
        {
            if (devices.TryGetValue(change.Device.DeviceId, out var existing))
            {
                // A duplicate start notification may supply richer metadata.
                devices[existing.DeviceId] = Merge(existing, change.Device);
                return null;
            }
            devices[change.Device.DeviceId] = change.Device;
            return change.Device.Record("System.USB.Events", change.Timestamp, "Connected", "Native");
        }
        if (!devices.Remove(change.Device.DeviceId, out var cached)) return null;
        return cached.Record("System.USB.Events", change.Timestamp, "Disconnected", "Native");
    }

    internal static UsbDevice Merge(UsbDevice old, UsbDevice current) => new(current.DeviceId,
        current.VendorId ?? old.VendorId, current.ProductId ?? old.ProductId,
        current.Manufacturer ?? old.Manufacturer, current.Product ?? old.Product,
        current.SerialNumber ?? old.SerialNumber, current.Location ?? old.Location);
}
