// <copyright file="OperatingSystemAspect.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Logship.DeviceIdentifier.Components
{
    public sealed class OperatingSystemAspect : BaseSynchronousAspect
    {
        private const string OperatingSystem = "OperatingSystem";

        public OperatingSystemAspect() : base(OperatingSystem)
        {

        }

        protected override string GetAspect()
        {
            var os = new OperatingSystem(Environment.OSVersion.Platform, Environment.Version);
            return os.ToString();
        }
    }
}

