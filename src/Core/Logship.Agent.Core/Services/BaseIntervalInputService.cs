// <copyright file="BaseIntervalInputService.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Logship.Agent.Core.Records;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;

namespace Logship.Agent.Core.Services
{
    public abstract class BaseIntervalInputService<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]TConfig
    > : BaseInputService<TConfig>
        where TConfig : BaseIntervalInputConfiguration, new()
    {
        public BaseIntervalInputService(TConfig? config, IEventBuffer buffer, string serviceName, ILogger logger)
            : base(config, buffer, serviceName, logger)
        {
        }

        protected override async Task ExecuteAsync(CancellationToken token)
        {
            while (false == token.IsCancellationRequested)
            {
                try
                {
                    await this.ExecuteSingleAsync(token);
                    await Task.Delay(this.Config.Interval, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { /* noop */ }
                catch (Exception ex)
                {
                    ServiceLog.ServiceException(this.Logger, this.ServiceName, this.ExitOnException, ex);
                    if (this.ExitOnException)
                    {
                        break;
                    }
                }
            }
        }

        protected abstract Task ExecuteSingleAsync(CancellationToken token);
    }
}

