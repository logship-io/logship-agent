namespace Logship.DeviceIdentifier
{
    public interface IDeviceIdentifierBuilder
    {
        IReadOnlyDictionary<string, string> ReadAspects();

        ValueTask AddAspectAsync(IAspectComponent aspectComponent, CancellationToken token);
    }
}
