// <copyright file="IDeviceIdentifierBuilder.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

namespace Logship.DeviceIdentifier
{
    public interface IDeviceIdentifierBuilder
    {
        IReadOnlyDictionary<string, string> ReadAspects();

        ValueTask AddAspectAsync(IAspectComponent aspectComponent, CancellationToken token);
    }
}

