// <copyright file="BaseInputService.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Logship.Agent.Core.Records;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

namespace Logship.Agent.Core.Services
{
    public abstract class BaseInputService<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]TConfig
    > : BaseConfiguredService<TConfig>
        where TConfig : BaseInputConfiguration, new()
    {
        protected IEventBuffer Buffer { get; init; }

        protected virtual bool ExitOnException { get; set; } = true;

        public BaseInputService(TConfig? config, IEventBuffer buffer, string serviceName, ILogger logger)
            : base(serviceName, config, logger)
        {
            this.Buffer = buffer;
        }

        protected static DataRecord CreateRecord(string schema, DateTimeOffset? timestamp = null)
        {
            timestamp ??= DateTimeOffset.UtcNow;
            var data = new Dictionary<string, object>()
            {
                { "machine", Environment.MachineName },
            };
            return new DataRecord(schema, timestamp.Value, data);
        }
    }
}

