using Logship.DeviceIdentifier.Formatter;
using System.Security.Cryptography;
using System.Text;

namespace Logship.Agent.DeviceIdentifier.Formatter
{
    public sealed class Sha512Formatter : IDeviceIdentifierFormatter
    {
        public string Format(IReadOnlyDictionary<string, string> values)
        {
            var sortedDict = new SortedDictionary<string, string>();
            foreach (var value in values)
            {
                sortedDict.Add(value.Key, value.Value);
            }

            if (sortedDict.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var value in sortedDict)
                {
                    sb.Append(value.Key);
                    sb.Append(": ");
                    sb.Append(value.Value);
                }
                byte[] hashBytes = SHA3_512.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
                return Convert.ToHexString(hashBytes);
            }

            return "none";
        }
    }
}
