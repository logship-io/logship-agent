// <copyright file="AgentHealthService.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Logship.Agent.Core.Services
{
    internal sealed class AgentHealthService : BaseAsyncService
    {
        private readonly DateTimeOffset startupTime;
        private readonly IEventBuffer sink;
        private TimeSpan interval;

        public AgentHealthService(IOptions<OutputConfiguration> config, IEventBuffer sink, ILogger<AgentHealthService> logger)
            : base(nameof(AgentHealthService), logger)
        {
            this.sink = sink;
            this.interval = config.Value.Health?.Interval ?? TimeSpan.FromSeconds(15);
            this.startupTime = DateTimeOffset.UtcNow;
        }

        protected override async Task ExecuteAsync(CancellationToken token)
        {
            while (false == token.IsCancellationRequested)
            {
                this.sink.Add(new Records.DataRecord("logship.agent.uptime", DateTimeOffset.UtcNow, new Dictionary<string, object>
                {
                    { "machine", Environment.MachineName },
                    { "startTime", this.startupTime.ToString("O") },
                    { "interval", this.interval.ToString("c") },
                    { "value", (DateTimeOffset.UtcNow - this.startupTime).ToString("c") },
                    { "counter", (DateTimeOffset.UtcNow - this.startupTime).TotalMilliseconds }
                }));

                await Task.Delay(this.interval, token).ConfigureAwait(false);
            }
        }
    }
}

