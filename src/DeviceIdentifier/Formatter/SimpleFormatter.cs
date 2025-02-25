using System.Text;

namespace Logship.DeviceIdentifier.Formatter
{
    public sealed class SimpleFormatter : IDeviceIdentifierFormatter
    {
        public string Format(IReadOnlyDictionary<string, string> values)
        {
            var sb = new StringBuilder();
            foreach (var kvp in values)
            {
                sb.Append(kvp.Key);
                sb.Append(": ");
                sb.AppendLine(kvp.Value); ;
            }

            return sb.ToString();
        }
    }
}
