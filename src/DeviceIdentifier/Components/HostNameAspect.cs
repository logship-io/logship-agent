namespace Logship.DeviceIdentifier.Components
{
    public sealed class HostNameAspect : BaseSynchronousAspect
    {
        private const string OperatingSystem = "HostName";

        public HostNameAspect() : base(OperatingSystem)
        {

        }

        protected override string GetAspect()
        {
            return Environment.MachineName;
        }
    }
}
