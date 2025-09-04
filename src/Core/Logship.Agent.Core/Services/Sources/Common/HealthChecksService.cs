// <copyright file="HealthChecksService.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Logship.Agent.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Logship.Agent.Core.Inputs.Common
{
    internal sealed class HealthChecksService : BaseInputService<HealthChecksConfiguration>
    {
        private readonly object _mutex = new object();
        private readonly IEventBuffer sink;
        private List<HealthCheckTarget> targets;
        private readonly List<Task<HealthCheckState>> healthChecks;

        public HealthChecksService(IOptions<SourcesConfiguration> config, IEventBuffer sink, ILogger<HealthChecksService> logger)
            : base(config.Value.HealthChecks, sink, nameof(HealthChecksService), logger)
        {
            this.sink = sink;
            this.targets = new List<HealthCheckTarget>();
            this.healthChecks = new List<Task<HealthCheckState>>();

            foreach (var target in this.Config.Targets)
            {
                if (false == Uri.TryCreate(target.Endpoint, UriKind.Absolute, out Uri? uri))
                {
                    HealthCheckLog.InvalidHealthCheckTarget(this.Logger, target.Endpoint);
                    continue;
                }

                this.targets.Add(new HealthCheckTarget(uri!, target.Interval, target.IncludeResponseBody, target.IncludeResponseHeaders));
            }
        }

        protected override async Task ExecuteAsync(CancellationToken token)
        {
            if (this.targets.Count == 0)
            {
                HealthCheckLog.SkipHealthChecks(this.Logger);
                return;
            }

            foreach (var target in targets)
            {
                HealthCheckLog.SkipHealthChecks(this.Logger, target.Endpoint, target.Interval);
                this.healthChecks.Add(PerformHealthCheck(target, token));
            }

            while (false == token.IsCancellationRequested)
            {
                try
                {
                    var completedTask = await Task.WhenAny(this.healthChecks);
                    var state = await completedTask;

                    lock (_mutex)
                    {
                        this.healthChecks.Remove(completedTask);
                        this.healthChecks.Add(PerformHealthCheckWithDelay(state.Target, state.Target.Interval, token));
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    HealthCheckLog.Error(this.Logger, ex);
                }
            }
        }

        private async Task<HealthCheckState> PerformHealthCheckWithDelay(HealthCheckTarget target, TimeSpan delay, CancellationToken token)
        {
            await Task.Delay(delay, token);
            return await PerformHealthCheck(target, token);
        }

        private async Task<HealthCheckState> PerformHealthCheck(HealthCheckTarget target, CancellationToken token)
        {
            using var client = new HttpClient();
            var start = DateTime.UtcNow;
            HealthCheckLog.StartedHealthCheck(this.Logger, target.Endpoint);
            HealthCheckState state;
            try
            {
                using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, target.Endpoint), token);
                var done = DateTime.UtcNow;
                state = new HealthCheckState(target, start, done, response);
                await PushResponse(state, token);
                HealthCheckLog.FinishedHealthCheck(this.Logger, target.Endpoint);

            }
            catch (Exception ex)
            {
                HealthCheckLog.Error(this.Logger, target.Endpoint, ex);
                state = new HealthCheckState(target, start, DateTime.UtcNow, ex);
                await PushException(state, token);
            }

            return state;
        }

        private async Task PushResponse(HealthCheckState state, CancellationToken token)
        {
            string headers = state.ResponseMessage != null && state.Target.IncludeResponseHeaders
                ? JsonSerializer.Serialize(state.ResponseMessage.Headers, HealthServiceSerializerContext.Default.HttpResponseHeaders)
                : string.Empty;
            string body = state.ResponseMessage != null && state.Target.IncludeResponseHeaders
                ? await state.ResponseMessage.Content.ReadAsStringAsync(token)
                : string.Empty;
            this.sink.Add(new Records.DataRecord("logship.agent.healthchecks", DateTimeOffset.UtcNow, new Dictionary<string, object>
            {
                { "machine", Environment.MachineName },
                { "endpoint", state.Target.Endpoint.ToString() },
                { "startTimeUtc", state.Start.ToString("O") },
                { "endTimeUtc", state.End.ToString("O") },
                { "interval", state.Target.Interval.ToString("c") },
                { "status", ((int?)state.ResponseMessage?.StatusCode) ?? -1 },
                { "latencyMillis", (int)(state.End - state.Start).TotalMilliseconds },
                { "headers", headers },
                { "body", body },
            }));
        }

        private Task PushException(HealthCheckState state, CancellationToken token)
        {
            this.sink.Add(new Records.DataRecord("logship.agent.healthchecks.exceptions", DateTimeOffset.UtcNow, new Dictionary<string, object>
            {
                { "machine", Environment.MachineName },
                { "endpoint", state.Target.Endpoint.ToString() },
                { "startTimeUtc", state.Start.ToString("O") },
                { "timestamp", state.End.ToString("O") },
                { "interval", state.Target.Interval.ToString("c") },
                { "message", state.Exception!.Message },
            }));

            return Task.CompletedTask;
        }

        private sealed class HealthCheckState
        {
            public HealthCheckTarget Target;
            public DateTime Start;
            public DateTime End;
            public HttpResponseMessage? ResponseMessage;
            public Exception? Exception;

            public HealthCheckState(HealthCheckTarget target, DateTime start, DateTime end, HttpResponseMessage? responseMessage)
            {
                Target = target;
                Start = start;
                End = end;
                ResponseMessage = responseMessage;
                Exception = null!;
            }

            public HealthCheckState(HealthCheckTarget target, DateTime start, DateTime end, Exception? exception)
            {
                Target = target;
                Start = start;
                End = end;
                ResponseMessage = null!;
                Exception = exception;
            }
        }
    }

    [JsonSerializable(typeof(HttpResponseHeaders))]
    internal sealed partial class HealthServiceSerializerContext : JsonSerializerContext
    {

    }

    sealed class HealthCheckTarget
    {
        public HealthCheckTarget(Uri endpoint, TimeSpan interval, bool includeResponseBody, bool includeResponseHeaders)
        {
            Endpoint = endpoint;
            Interval = interval;
            IncludeResponseBody = includeResponseBody;
            IncludeResponseHeaders = includeResponseHeaders;
        }

        public Uri Endpoint { get; set; }

        public TimeSpan Interval { get; set; }

        public bool IncludeResponseHeaders { get; set; }

        public bool IncludeResponseBody { get; set; }
    }

    internal static partial class HealthCheckLog
    {
        [LoggerMessage(LogLevel.Warning, "Invalid HealthCheck Target Endpoint: {Uri}")]
        public static partial void InvalidHealthCheckTarget(ILogger logger, string uri);

        [LoggerMessage(LogLevel.Information, "No valid HealthChecks defined. Ending Service Execution.")]
        public static partial void SkipHealthChecks(ILogger logger);

        [LoggerMessage(LogLevel.Information, "Initializing HealthCheck for endpoint {Uri} with interval {Interval}")]
        public static partial void SkipHealthChecks(ILogger logger, Uri uri, TimeSpan interval);

        [LoggerMessage(LogLevel.Debug, "Starting HealthCheck on {Endpoint}")]
        public static partial void StartedHealthCheck(ILogger logger, Uri endpoint);

        [LoggerMessage(LogLevel.Debug, "Finished HealthCheck on {Endpoint}")]
        public static partial void FinishedHealthCheck(ILogger logger, Uri endpoint);

        [LoggerMessage(LogLevel.Warning, "HealthCheck on endpoint {Endpoint} failed.")]
        public static partial void Error(ILogger logger, Uri endpoint, Exception exception);

        [LoggerMessage(LogLevel.Information, "HealthCheck failed.")]
        public static partial void Error(ILogger logger, Exception exception);
    }
}

