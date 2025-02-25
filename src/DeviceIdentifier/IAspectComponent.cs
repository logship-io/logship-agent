namespace Logship.DeviceIdentifier
{
    public interface IAspectComponent
    {
        ValueTask<Dictionary<string, string>> GetAspectAsync(CancellationToken token);
    }
}
