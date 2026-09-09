// <copyright file="PrometheusMetricsService.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Logship.Agent.Core.Services.Sources.Common.Prometheus
{
    internal sealed class PrometheusMetricsService : BaseInputService<PrometheusConfiguration>
    {
        private static readonly HashSet<string> ReservedColumns = new HashSet<string>(StringComparer.Ordinal) { "machine", "endpoint", "name", "value" };

        private readonly IHttpClientFactory httpClientFactory;
        private readonly List<PrometheusTarget> targets;

        public PrometheusMetricsService(IOptions<SourcesConfiguration> config, IEventBuffer buffer, IHttpClientFactory httpClientFactory, ILogger<PrometheusMetricsService> logger)
            : base(config.Value.Prometheus, buffer, nameof(PrometheusMetricsService), logger)
        {
            this.httpClientFactory = httpClientFactory;
            this.targets = new List<PrometheusTarget>();

            foreach (var target in this.Config.Targets)
            {
                if (false == Uri.TryCreate(target.Endpoint, UriKind.Absolute, out Uri? uri))
                {
                    PrometheusLog.InvalidTargetEndpoint(this.Logger, target.Endpoint);
                    continue;
                }

                this.targets.Add(new PrometheusTarget(uri!, target.Interval, target.Headers));
            }
        }

        protected override async Task ExecuteAsync(CancellationToken token)
        {
            if (this.targets.Count == 0)
            {
                PrometheusLog.NoTargets(this.Logger);
                return;
            }

            var scrapes = new List<Task<PrometheusTarget>>(this.targets.Count);
            foreach (var target in this.targets)
            {
                PrometheusLog.InitializingTarget(this.Logger, target.Endpoint, target.Interval);
                scrapes.Add(this.ScrapeAsync(target, token));
            }

            while (false == token.IsCancellationRequested)
            {
                try
                {
                    var completed = await Task.WhenAny(scrapes);
                    var target = await completed;
                    scrapes.Remove(completed);
                    scrapes.Add(this.ScrapeAfterDelayAsync(target, token));
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { /* noop */ }
            }
        }

        private async Task<PrometheusTarget> ScrapeAfterDelayAsync(PrometheusTarget target, CancellationToken token)
        {
            await Task.Delay(target.Interval, token);
            return await this.ScrapeAsync(target, token);
        }

        private async Task<PrometheusTarget> ScrapeAsync(PrometheusTarget target, CancellationToken token)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, target.Endpoint);
                foreach (var header in target.Headers)
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                using var client = httpClientFactory.CreateClient();
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                var scrapeTime = DateTimeOffset.UtcNow;
                using var body = await response.Content.ReadAsStreamAsync(token);
                var result = await PrometheusTextParser.ParseAsync(body, token);
                if (result.MalformedLines > 0)
                {
                    PrometheusLog.MalformedLines(this.Logger, result.MalformedLines, target.Endpoint);
                }

                int nonFiniteValues = 0;
                foreach (var sample in result.Samples)
                {
                    // Non-finite values cannot be serialized by the upload pipeline.
                    if (false == double.IsFinite(sample.Value))
                    {
                        nonFiniteValues++;
                        continue;
                    }

                    var record = CreateRecord("Prometheus.Metrics", sample.Timestamp ?? scrapeTime);
                    record.Data["endpoint"] = target.Endpoint.ToString();
                    record.Data["name"] = sample.Name;
                    record.Data["value"] = sample.Value;
                    foreach (var label in sample.Labels)
                    {
                        // Dots are stripped from column names downstream (see DataRecord.SanitizeRecord),
                        // so map UTF-8 label names like "label.with.dots" to "label_with_dots" here.
                        var column = label.Key.Replace('.', '_');
                        if (ReservedColumns.Contains(column))
                        {
                            column = "label_" + column;
                        }

                        record.Data[column] = label.Value;
                    }

                    this.Buffer.Add(record);
                }

                if (nonFiniteValues > 0)
                {
                    PrometheusLog.NonFiniteValues(this.Logger, nonFiniteValues, target.Endpoint);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                PrometheusLog.ScrapeFailed(this.Logger, target.Endpoint, ex);
            }

            return target;
        }

        private sealed record PrometheusTarget(Uri Endpoint, TimeSpan Interval, IReadOnlyDictionary<string, string> Headers);
    }

    internal static partial class PrometheusLog
    {
        [LoggerMessage(LogLevel.Warning, "Invalid Prometheus target endpoint: {Endpoint}")]
        public static partial void InvalidTargetEndpoint(ILogger logger, string endpoint);

        [LoggerMessage(LogLevel.Information, "No valid Prometheus targets defined. Ending service execution.")]
        public static partial void NoTargets(ILogger logger);

        [LoggerMessage(LogLevel.Information, "Initializing Prometheus scrape for endpoint {Endpoint} with interval {Interval}")]
        public static partial void InitializingTarget(ILogger logger, Uri endpoint, TimeSpan interval);

        [LoggerMessage(LogLevel.Warning, "Prometheus scrape failed for endpoint {Endpoint}")]
        public static partial void ScrapeFailed(ILogger logger, Uri endpoint, Exception exception);

        [LoggerMessage(LogLevel.Warning, "Skipped {Count} malformed Prometheus lines from endpoint {Endpoint}")]
        public static partial void MalformedLines(ILogger logger, int count, Uri endpoint);

        [LoggerMessage(LogLevel.Debug, "Skipped {Count} non-finite Prometheus values from endpoint {Endpoint}")]
        public static partial void NonFiniteValues(ILogger logger, int count, Uri endpoint);
    }
}
