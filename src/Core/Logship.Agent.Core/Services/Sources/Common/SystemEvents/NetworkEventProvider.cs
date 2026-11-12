using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Logship.Agent.Core.Services.Sources.Common.SystemEvents;

internal sealed class NetworkEventProvider : ISystemEventProvider
{
    public IReadOnlyList<SystemEntity> Snapshot()
    {
        var entities = new List<SystemEntity>();
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
        {
            var properties = network.GetIPProperties();
            var index = InterfaceIndex(network);
            var entity = Interface(index, network.Name, network.Id);
            entities.Add(entity);
            if (network.OperationalStatus == OperationalStatus.Up) entities.Add(Link(index, network.Name));
            foreach (var address in properties.UnicastAddresses)
                entities.Add(Address(index, address.Address, address.PrefixLength, network.Name));
        }
        entities.AddRange(OperatingSystem.IsWindows() ? WindowsNetworkEvents.Routes() : LinuxNetworkEvents.Routes());
        return entities;
    }

    public IDisposable Subscribe(Action<SystemChange> notify) => OperatingSystem.IsWindows()
        ? WindowsNetworkEvents.Subscribe(notify) : LinuxNetworkEvents.Subscribe(notify);

    private static int InterfaceIndex(NetworkInterface network)
    {
        if (OperatingSystem.IsWindows())
        {
            var guid = Guid.Parse(network.Id);
            var error = ConvertInterfaceGuidToLuid(ref guid, out var luid);
            if (error != 0) throw new System.ComponentModel.Win32Exception((int)error);
            // Retrieve the index even for adapters with neither IP protocol enabled.
            error = ConvertInterfaceLuidToIndex(ref luid, out var result);
            if (error != 0) throw new System.ComponentModel.Win32Exception((int)error);
            return (int)result;
        }
        var linuxIndex = if_nametoindex(network.Name);
        if (linuxIndex == 0) throw new IOException("Network interface disappeared during inventory.");
        return (int)linuxIndex;
    }

    [DllImport("iphlpapi.dll")] private static extern uint ConvertInterfaceGuidToLuid(ref Guid guid, out ulong luid);
    [DllImport("iphlpapi.dll")] private static extern uint ConvertInterfaceLuidToIndex(ref ulong luid, out uint index);
#pragma warning disable CA2101 // Linux interface names are UTF-8.
    [DllImport("libc")] private static extern uint if_nametoindex([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
#pragma warning restore CA2101

    internal static string Index(uint index) => index.ToString(CultureInfo.InvariantCulture);
    internal static SystemEntity Interface(int index, string? name = null, string? id = null)
    {
        var fields = Fields(index, name);
        if (id != null) fields["InterfaceId"] = id;
        return new("Interface:" + Index((uint)index), "InterfaceAdded", "InterfaceRemoved", fields);
    }
    internal static SystemEntity Link(int index, string? name = null) => new("Link:" + Index((uint)index), "LinkUp", "LinkDown", Fields(index, name));
    internal static SystemEntity Address(int index, IPAddress address, int prefix, string? name = null)
    {
        // Scope belongs to the interface identity, not the address text.
        var value = new IPAddress(address.GetAddressBytes()).ToString();
        var fields = Fields(index, name);
        fields["Address"] = value; fields["PrefixLength"] = prefix;
        return new("Address:" + Index((uint)index) + ":" + value, "AddressAdded", "AddressRemoved", fields, prefix.ToString(CultureInfo.InvariantCulture));
    }
    internal static SystemEntity Route(int index, IPAddress destination, int prefix, IPAddress gateway, uint metric, uint table = 0, uint type = 1)
    {
        var fields = Fields(index);
        var target = new IPAddress(destination.GetAddressBytes()).ToString();
        var hop = new IPAddress(gateway.GetAddressBytes()).ToString();
        fields["Destination"] = target; fields["PrefixLength"] = prefix;
        fields["Gateway"] = hop; fields["Metric"] = metric; fields["Table"] = table; fields["RouteType"] = type;
        return new("Route:" + Index((uint)index) + ":" + target + "/" + prefix.ToString(CultureInfo.InvariantCulture)
            + ":" + hop + ":" + Index(table) + ":" + Index(type), "RouteAdded", "RouteRemoved", fields, Index(metric));
    }
    private static Dictionary<string, object> Fields(int index, string? name = null)
    {
        var fields = new Dictionary<string, object> { ["InterfaceIndex"] = index };
        if (!string.IsNullOrEmpty(name)) fields["InterfaceName"] = name;
        return fields;
    }
}
