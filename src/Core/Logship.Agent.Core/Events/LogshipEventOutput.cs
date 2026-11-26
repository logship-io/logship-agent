// <copyright file="LogshipEventOutput.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Internals;
using Logship.Agent.Core.Records;
using Logship.Agent.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net;

namespace Logship.Agent.Core.Events
{
    internal sealed class LogshipEventOutput : IEventOutput, IDisposable
    {
        internal static readonly ActivitySource OutputActivitySource = new("Logship.Agent.Output", "1.0.0");

        private readonly string endpoint;
        private readonly Guid account;
        private readonly IOutputAuth authenticator;
        private readonly AgentAuthenticationService authentication;
        private readonly ILogger<LogshipEventOutput> logger;
        private readonly ILogger lifecycleLogger;
        private readonly HttpClient client;
        private int firstBatchSent;

        public LogshipEventOutput(IOptions<OutputConfiguration> config, IOutputAuth authenticator, AgentAuthenticationService authentication, IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
        {
            this.endpoint = config.Value.Endpoint;
            this.account = config.Value.Account;
            this.authenticator = authenticator;
            this.authentication = authentication;
            this.logger = loggerFactory.CreateLogger<LogshipEventOutput>();
            this.lifecycleLogger = loggerFactory.CreateLogger("Logship.Agent.Lifecycle");
            this.client = httpClientFactory.CreateClient();
            EventsLog.Endpoint(this.logger, this.endpoint, this.account);
        }

        public void Dispose()
        {
            ((IDisposable)client).Dispose();
            GC.SuppressFinalize(this);
        }

        public async Task<bool> SendAsync(IReadOnlyCollection<DataRecord> records, CancellationToken cancellationToken)
        {
            if (records.Count == 0)
            {
                return true;
            }

            if (!this.authentication.InitialAuthenticationComplete)
            {
                return false;
            }

            using var activity = OutputActivitySource.StartActivity("HttpPush", ActivityKind.Client);
            activity?.SetTag("logship.output.endpoint", this.endpoint);
            activity?.SetTag("logship.output.record_count", records.Count);

            using var request = await Api.PutInflowAsync(this.endpoint, this.account, records, cancellationToken);
            if (false == await this.authenticator.TryAddAuthAsync(request, cancellationToken))
            {
                EventOutputLog.NoPushAuthorization(this.logger);
                return false;
            }

            using var sendScope = this.logger.BeginScope(new Dictionary<string, object>
            {
                ["Records"] = records.Count,
                ["Endpoint"] = this.endpoint,
            });

            try
            {
                EventOutputLog.PushingMetrics(this.logger);
                using var response = await this.client.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    EventOutputLog.PushingMetricsSuccessful(this.logger);
                    if (Interlocked.Exchange(ref this.firstBatchSent, 1) == 0)
                    {
                        EventOutputLog.FirstBatchSent(this.lifecycleLogger, records.Count);
                    }
                }
                else
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        await this.authenticator.InvalidateAsync(cancellationToken);
                    }

                    EventOutputLog.PushingMetricsFailed(this.logger, response.RequestMessage!.RequestUri, response.StatusCode, response.Content.ToString() ?? string.Empty);
                    return false;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return false; }
            catch (Exception ex)
            {
                EventOutputLog.PushingMetricsException(this.logger, ex);
                throw;
            }

            return true;
        }
    }

    internal static partial class EventOutputLog
    {
        [LoggerMessage(LogLevel.Information, "Connected to Logship and pushed first batch ({Count} records).")]
        public static partial void FirstBatchSent(ILogger logger, int count);

        [LoggerMessage(LogLevel.Debug, "Pushing metrics...")]
        public static partial void PushingMetrics(ILogger logger);

        [LoggerMessage(LogLevel.Debug, "Successfully pushed metrics.")]
        public static partial void PushingMetricsSuccessful(ILogger logger);

        [LoggerMessage(LogLevel.Error, "Failed to push HTTP metrics to {RequestUrl}. {Status} {Message}")]
        public static partial void PushingMetricsFailed(ILogger logger, Uri? requestUrl, HttpStatusCode status, string message);

        [LoggerMessage(LogLevel.Warning, "No authorization available for HTTP Metrics push. Skipping")]
        public static partial void NoPushAuthorization(ILogger logger);

        [LoggerMessage(LogLevel.Error, "Failed to push metrics.")]
        public static partial void PushingMetricsException(ILogger logger, Exception exception);

    }
}

