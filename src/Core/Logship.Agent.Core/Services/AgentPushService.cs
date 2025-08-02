// <copyright file="AgentPushService.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Logship.Agent.Core.Services
{
    internal sealed class AgentPushService : BaseAsyncService
    {
        private readonly TimeSpan interval;
        private readonly IEventSink eventSink;

        public AgentPushService(IOptions<OutputConfiguration> config, IEventSink eventSink, ILogger<AgentPushService> logger)
            : base(nameof(AgentPushService), logger)
        {
            this.interval = config.Value.Interval;
            this.eventSink = eventSink;
        }

        protected override async Task ExecuteAsync(CancellationToken token)
        {
            while (false == token.IsCancellationRequested)
            {
                try
                {
                    // Delay for the configured push interval
                    await Task.Delay(this.interval, token);
                    AgentPushServiceLog.StartingFlush(Logger);
                    await this.eventSink.FlushAsync(token);
                    AgentPushServiceLog.FinishedFlush(Logger);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    AgentPushServiceLog.FlushError(Logger, ex);
                }
            }
        }
    }

    internal static partial class AgentPushServiceLog
    {
        [LoggerMessage(LogLevel.Trace, "Starting event sink flush.")]
        public static partial void StartingFlush(ILogger logger);

        [LoggerMessage(LogLevel.Trace, "Successfully flushed metrics.")]
        public static partial void FinishedFlush(ILogger logger);

        [LoggerMessage(LogLevel.Trace, "Failed to flush event sink.")]
        public static partial void FlushError(ILogger logger, Exception exception);
    }
}

