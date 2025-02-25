using System.Globalization;
using System.Net.NetworkInformation;
using System.Text;

namespace Logship.DeviceIdentifier.Components
{
    public sealed class MacAddressAspect : BaseSynchronousAspect
    {
        private const string MacAddress = "MacAddress";
        private const string DockerBridgeInterface = "docker0";
        private const string EmptyMacAddress = "00:00:00:00:00:00";

        public MacAddressAspect() : base(MacAddress)
        {

        }

        private static string Format(PhysicalAddress physicalAddress)
        {
            byte[] bytes = physicalAddress.GetAddressBytes();
            var sb = new StringBuilder(20);
            for (int i = 0; i < bytes.Length; i++)
            {
                sb.Append(bytes[i].ToString("x", CultureInfo.InvariantCulture).PadLeft(2, '0'));
                if (i < bytes.Length - 1)
                {
                    sb.Append(':');
                }
            }

            return sb.ToString();
        }

        protected override string GetAspect()
        {
            return string.Join(',', NetworkInterface.GetAllNetworkInterfaces()
                .Where(x => (x.NetworkInterfaceType != NetworkInterfaceType.Wireless80211) && (x.Name != DockerBridgeInterface))
                .Select(x => Format(x.GetPhysicalAddress()))
                .Where(x => x.Length > 0 && !string.Equals(x, EmptyMacAddress, StringComparison.Ordinal)));
        }
    }
}
