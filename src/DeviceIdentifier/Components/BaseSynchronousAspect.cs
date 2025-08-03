// <copyright file="BaseSynchronousAspect.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

namespace Logship.DeviceIdentifier.Components
{
    public abstract class BaseSynchronousAspect : IAspectComponent
    {
        private readonly string Key;
        protected BaseSynchronousAspect(string key)
        {
            this.Key = key;
        }

        protected abstract string GetAspect();

        public ValueTask<Dictionary<string, string>> GetAspectAsync(CancellationToken token)
        {
            var result = new Dictionary<string, string>
            {
                { Key, GetAspect() }
            };
            return ValueTask.FromResult(result);
        }
    }
}

