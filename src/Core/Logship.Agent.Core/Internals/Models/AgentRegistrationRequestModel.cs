using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Logship.Agent.Core.Internals.Models
{
    internal sealed record AgentRegistrationRequestModel(string Name, string HostName, string MachineIdentifier, IReadOnlyList<KeyValuePair<string, string>> Attributes);
}
