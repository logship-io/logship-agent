using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Logship.DeviceIdentifier.Formatter
{
    public interface IDeviceIdentifierFormatter
    {
        public string Format(IReadOnlyDictionary<string, string> values);
    }
}
