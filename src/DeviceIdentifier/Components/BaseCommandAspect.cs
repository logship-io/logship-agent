// <copyright file="BaseCommandAspect.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

namespace Logship.DeviceIdentifier.Components
{
    public abstract class BaseCommandAspect : IAspectComponent
    {
        protected abstract ValueTask<Dictionary<string, string>> ExecuteAsync(CancellationToken token);

        public ValueTask<Dictionary<string, string>> GetAspectAsync(CancellationToken token)
        {
            return this.ExecuteAsync(token);
        }

        protected static async Task<string> ReadFileAsync(string path, CancellationToken token)
        {
            if (File.Exists(path))
            {
                return await File.ReadAllTextAsync(path, token);
            }

            return string.Empty;
        }
    }
}

