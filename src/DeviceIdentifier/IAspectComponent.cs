// <copyright file="IAspectComponent.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

namespace Logship.DeviceIdentifier
{
    public interface IAspectComponent
    {
        ValueTask<Dictionary<string, string>> GetAspectAsync(CancellationToken token);
    }
}

