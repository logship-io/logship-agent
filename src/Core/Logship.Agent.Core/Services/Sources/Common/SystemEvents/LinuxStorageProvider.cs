using System.Globalization;

namespace Logship.Agent.Core.Services.Sources.Common.SystemEvents;

internal sealed class LinuxStorageProvider : ISystemEventProvider
{
    public IReadOnlyList<SystemEntity> Snapshot()
    {
        var entities = new List<SystemEntity>();
        foreach (var path in Directory.GetDirectories("/sys/class/block"))
        {
            var name = Path.GetFileName(path);
            var partition = File.Exists(Path.Combine(path, "partition"));
            var properties = ReadProperties(path);
            properties.TryGetValue("ID_FS_LABEL", out var label);
            properties.TryGetValue("ID_FS_TYPE", out var fileSystem);
            properties.TryGetValue("ID_FS_UUID", out var uuid);
            entities.Add(Device(name, partition, label, fileSystem, uuid));
            if (!partition && !string.IsNullOrEmpty(fileSystem)) entities.Add(Device(name, true, label, fileSystem, uuid));
        }
        entities.AddRange(ParseMounts(File.ReadAllLines("/proc/self/mountinfo")));
        return entities;
    }

    private static Dictionary<string, string> ReadProperties(string path)
    {
        var result = new Dictionary<string, string>();
        try
        {
            var number = File.ReadAllText(Path.Combine(path, "dev")).Trim();
            foreach (var line in File.ReadAllLines("/run/udev/data/b" + number))
            {
                var equals = line.IndexOf('=');
                if (line.StartsWith("E:", StringComparison.Ordinal) && equals > 2) result[line[2..equals]] = line[(equals + 1)..];
            }
        }
        catch (IOException) { /* Optional metadata may be absent or the device may have disappeared. */ }
        catch (UnauthorizedAccessException) { }
        return result;
    }

    internal static SystemEntity Device(string name, bool partition, string? label = null, string? fileSystem = null, string? uuid = null)
    {
        var kind = partition ? "Volume" : "Disk";
        var fields = new Dictionary<string, object> { ["DeviceId"] = "/dev/" + name, ["Kind"] = kind };
        if (!string.IsNullOrEmpty(label)) fields["VolumeLabel"] = label;
        if (!string.IsNullOrEmpty(fileSystem)) fields["FileSystem"] = fileSystem;
        if (!string.IsNullOrEmpty(uuid)) fields["VolumeUuid"] = uuid;
        return new(kind + ":" + name, kind + "Added", kind + "Removed", fields);
    }

    internal static IReadOnlyList<SystemEntity> ParseMounts(IEnumerable<string> lines)
    {
        var result = new List<SystemEntity>();
        foreach (var line in lines)
        {
            var columns = line.Split(' ');
            var separator = Array.IndexOf(columns, "-");
            if (separator < 6 || separator + 3 >= columns.Length || !int.TryParse(columns[0], CultureInfo.InvariantCulture, out var mountId))
                throw new IOException("Invalid mountinfo entry; refusing incomplete mount inventory.");
            var location = Unescape(columns[4]);
            var device = Unescape(columns[separator + 2]);
            result.Add(new("Mount:" + columns[0] + ":" + columns[2] + ":" + location + ":" + Unescape(columns[3]), "Mounted", "Unmounted",
                new() { ["Kind"] = "Mount", ["MountId"] = mountId, ["DeviceId"] = device,
                    ["MountLocation"] = location, ["FileSystem"] = columns[separator + 1], ["MountRoot"] = Unescape(columns[3]) }));
        }
        return result;
    }

    // Decode in one pass: a literal backslash followed by digits must not be decoded twice.
    internal static string Unescape(string value)
    {
        var result = new System.Text.StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 3 < value.Length && value[i + 1] is >= '0' and <= '7'
                && value[i + 2] is >= '0' and <= '7' && value[i + 3] is >= '0' and <= '7')
            {
                result.Append((char)((value[i + 1] - '0') * 64 + (value[i + 2] - '0') * 8 + value[i + 3] - '0'));
                i += 3;
            }
            else result.Append(value[i]);
        }
        return result.ToString();
    }

    public IDisposable Subscribe(Action<SystemChange> notify) => UdevEvents.Subscribe("block", device =>
    {
        var action = UdevEvents.Action(device);
        var name = UdevEvents.Name(device);
        if (name == null) return;
        var partition = UdevEvents.Property(device, "DEVTYPE") == "partition";
        var fileSystem = UdevEvents.Property(device, "ID_FS_TYPE");
        if (action is "add" or "remove")
        {
            var entity = Device(name, partition,
                UdevEvents.Property(device, "ID_FS_LABEL"), fileSystem, UdevEvents.Property(device, "ID_FS_UUID"));
            notify(new(entity, action == "add", DateTimeOffset.UtcNow));
            if (!partition && (action == "remove" || !string.IsNullOrEmpty(fileSystem)))
                notify(new(Device(name, true, UdevEvents.Property(device, "ID_FS_LABEL"), fileSystem, UdevEvents.Property(device, "ID_FS_UUID")), action == "add", DateTimeOffset.UtcNow));
        }
        else notify(SystemChange.Signal());
    }, ex => notify(new(null, false, DateTimeOffset.UtcNow, ex)));
}
