namespace Logship.Agent.Core.Configuration;

public sealed class StorageEventsConfiguration : BaseIntervalInputConfiguration
{
    public StorageEventsConfiguration() { Enabled = false; Interval = TimeSpan.FromSeconds(5); }
}

public sealed class NetworkEventsConfiguration : BaseIntervalInputConfiguration
{
    public NetworkEventsConfiguration() { Enabled = false; Interval = TimeSpan.FromSeconds(15); }
}

public sealed class PowerEventsConfiguration : BaseInputConfiguration
{
    public PowerEventsConfiguration() => Enabled = false;
}
