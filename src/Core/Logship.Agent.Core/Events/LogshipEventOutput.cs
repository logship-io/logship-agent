// <copyright file="LogshipEventOutput.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Internals;
using Logship.Agent.Core.Records;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;

namespace Logship.Agent.Core.Events
{
    internal sealed class LogshipEventOutput : IEventOutput, IDisposable
    {
        private readonly string endpoint;
        private readonly Guid account;
        private readonly IOutputAuth authenticator;
        private readonly ILogger<LogshipEventOutput> logger;
        private readonly HttpClient client;

        public LogshipEventOutput(IOptions<OutputConfiguration> config, IOutputAuth authenticator, IHttpClientFactory httpClientFactory, ILogger<LogshipEventOutput> logger)
        {
            this.endpoint = config.Value.Endpoint;
            this.account = config.Value.Account;
            this.authenticator = authenticator;
            this.logger = logger;
            this.client = httpClientFactory.CreateClient();
            EventsLog.Endpoint(logger, this.endpoint, this.account);
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
                var response = await this.client.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    EventOutputLog.PushingMetricsSuccessful(this.logger);
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { /* noop */ }
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

