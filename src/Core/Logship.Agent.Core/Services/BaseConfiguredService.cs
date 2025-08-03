// <copyright file="BaseConfiguredService.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;

namespace Logship.Agent.Core.Services
{
    public abstract class BaseConfiguredService<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]TConfig
        > : BaseAsyncService
        where TConfig : BaseInputConfiguration, new()
    {
        protected TConfig Config { get; }

        protected BaseConfiguredService(string serviceName, TConfig? config, ILogger logger)
            : base(serviceName, logger)
        {
            if (config == null)
            {
                this.Config = new TConfig();
                this.Enabled = false;
                return;
            }

            this.Config = config!;
            this.Enabled = config!.Enabled;
        }
    }
}
