using System.Buffers.Binary;
using System.Text;

namespace Logship.Agent.Core.Services.Sources.Linux.Ieee80211;

internal static class Ieee80211FrameParser
{
    internal static Dictionary<string, object>? Parse(ReadOnlySpan<byte> packet, int linkType)
    {
        var data = new Dictionary<string, object>();
        int offset = 0;
        bool fcs = false;
        if (linkType == 127)
        {
            if (packet.Length < 8 || packet[0] != 0) return null;
            offset = BinaryPrimitives.ReadUInt16LittleEndian(packet[2..]);
            if (offset < 8 || offset > packet.Length) return null;
            uint present = BinaryPrimitives.ReadUInt32LittleEndian(packet[4..]);
            int position = 8;
            uint extension = present;
            while ((extension & 0x80000000) != 0)
            {
                if (position + 4 > offset) return null;
                extension = BinaryPrimitives.ReadUInt32LittleEndian(packet[position..]);
                position += 4;
            }
            // Only early standard fields are needed; later namespaces cannot change these.
            int[] sizes = [8, 1, 1, 4, 2, 1];
            int[] alignments = [8, 1, 1, 2, 2, 1];
            for (int bit = 0; bit <= 5; bit++)
            {
                if ((present & (1U << bit)) == 0) continue;
                position = (position + alignments[bit] - 1) & ~(alignments[bit] - 1);
                if (position + sizes[bit] > offset) return null;
                if (bit == 1)
                {
                    byte flags = packet[position];
                    if ((flags & 0x40) != 0) return null;
                    fcs = (flags & 0x10) != 0;
                }
                if (bit == 3)
                {
                    int frequency = BinaryPrimitives.ReadUInt16LittleEndian(packet[position..]);
                    data["FrequencyMHz"] = frequency;
                    int channel = FrequencyToChannel(frequency);
                    if (channel != 0) data["Channel"] = channel;
                    if (frequency >= 2400 && frequency <= 2500) data["Band"] = "2.4GHz";
                    else if (frequency >= 4900 && frequency < 5925) data["Band"] = "5GHz";
                }
                if (bit == 5) data["SignalDbm"] = (int)(sbyte)packet[position];
                position += sizes[bit];
            }
        }
        else if (linkType != 105) return null;
        var frame = packet[offset..];
        if (fcs)
        {
            if (frame.Length < 4) return null;
            frame = frame[..^4];
        }
        if (frame.Length < 24) return null;
        ushort control = BinaryPrimitives.ReadUInt16LittleEndian(frame);
        if ((control & 0x0f) != 0 || (control & 0x4000) != 0) return null;
        int subtype = (control >> 4) & 15;
        if (subtype != 4 && subtype != 5 && subtype != 8) return null;
        int elementsOffset = subtype == 4 ? 24 : 36;
        if (frame.Length < elementsOffset) return null;
        data["FrameType"] = subtype == 4 ? "ProbeRequest" : subtype == 5 ? "ProbeResponse" : "Beacon";
        data["DestinationMac"] = Mac(frame[4..10]);
        data["SourceMac"] = Mac(frame[10..16]);
        data["Bssid"] = Mac(frame[16..22]);
        ushort sequence = BinaryPrimitives.ReadUInt16LittleEndian(frame[22..]);
        data["SequenceNumber"] = sequence >> 4;
        data["FragmentNumber"] = sequence & 15;
        data["Retry"] = (control & 0x800) != 0;
        data["FrameLength"] = frame.Length;
        if (subtype != 4)
        {
            data["BeaconIntervalTu"] = (int)BinaryPrimitives.ReadUInt16LittleEndian(frame[32..]);
            int capabilities = BinaryPrimitives.ReadUInt16LittleEndian(frame[34..]);
            data["CapabilityFlags"] = capabilities;
            data["Privacy"] = (capabilities & 16) != 0;
        }
        var elements = frame[elementsOffset..];
        data["InformationElementsHex"] = Convert.ToHexString(elements);
        while (!elements.IsEmpty)
        {
            if (elements.Length < 2 || elements[1] > elements.Length - 2) return null;
            int id = elements[0], length = elements[1];
            var value = elements.Slice(2, length);
            if (id == 0 && !data.ContainsKey("Ssid"))
            {
                if (length > 32) return null;
                data["Ssid"] = Encoding.UTF8.GetString(value);
                data["SsidHex"] = Convert.ToHexString(value);
                data[subtype == 4 ? "WildcardProbe" : "HiddenSsid"] = length == 0;
            }
            if (id == 3 && length == 1) data["AdvertisedChannel"] = (int)value[0];
            elements = elements[(length + 2)..];
        }
        return data;
    }

    internal static int FrequencyToChannel(int frequency) => frequency switch
    {
        2484 => 14,
        >= 2412 and <= 2472 when (frequency - 2407) % 5 == 0 => (frequency - 2407) / 5,
        >= 4910 and < 5000 when frequency % 5 == 0 => (frequency - 4000) / 5,
        >= 5000 and < 5925 when frequency % 5 == 0 => (frequency - 5000) / 5,
        _ => 0,
    };

    private static string Mac(ReadOnlySpan<byte> bytes) => string.Join(":", bytes.ToArray().Select(b => b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
}
