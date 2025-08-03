// <copyright file="IDeviceIdentifierBuilderExtensions.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.DeviceIdentifier.Formatter;

namespace Logship.DeviceIdentifier
{
    public static class IDeviceIdentifierBuilderExtensions
    {
        public static string Build(this IDeviceIdentifierBuilder builder, IDeviceIdentifierFormatter format)
        {
            return format.Format(builder.ReadAspects());
        }

        public static async Task AddAspectsAsync(this IDeviceIdentifierBuilder builder, IReadOnlyList<IAspectComponent> aspects, CancellationToken token)
        {
            var results = new List<Task>(aspects.Count);
            foreach (IAspectComponent aspect in aspects)
            {
                await builder.AddAspectAsync(aspect, token);
            }
        }

    }
}

