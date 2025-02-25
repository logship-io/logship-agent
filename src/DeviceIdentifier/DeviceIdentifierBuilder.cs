using Logship.DeviceIdentifier.Formatter;
using System.Diagnostics;

namespace Logship.DeviceIdentifier
{
    [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
    public sealed class DeviceIdentifierBuilder : IDeviceIdentifierBuilder
    {
        private readonly Dictionary<string, string> aspects = new();

        public async ValueTask AddAspectAsync(IAspectComponent aspectComponent, CancellationToken token)
        {
            var result = await aspectComponent.GetAspectAsync(token);
            if (result == null || result.Count == 0)
            {
                return;
            }

            foreach (var aspect in result)
            {
                this.aspects.Add(aspect.Key, aspect.Value);
            }
        }

        public IReadOnlyDictionary<string, string> ReadAspects()
        {
            return aspects;
        }

        private string GetDebuggerDisplay()
        {
            var formatter = new SimpleFormatter();
            return formatter.Format(aspects);
        }
    }
}
