// <copyright file="HostNameAspect.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

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

