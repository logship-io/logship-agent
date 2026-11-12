namespace Logship.Agent.Core.Configuration;

public sealed class UsbConfiguration : BaseIntervalInputConfiguration
{
    public UsbConfiguration()
    {
        Enabled = false;
        Interval = TimeSpan.FromSeconds(30);
    }
}
