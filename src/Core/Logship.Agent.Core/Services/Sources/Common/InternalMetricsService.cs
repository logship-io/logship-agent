// <copyright file="InternalMetricsService.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Logship.Agent.Core.Records;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Logship.Agent.Core.Services.Sources.Common
{
    internal sealed class InternalMetricsService : BaseIntervalInputService<InternalMetricsConfiguration>
    {
        private readonly MeterListener? meterListener;
        private readonly ActivityListener? activityListener;
        private readonly object metricsLock = new();
        private readonly List<DataRecord> pendingMetrics = new();
        private readonly List<DataRecord> pendingTraces = new();
        private readonly object tracesLock = new();

        public InternalMetricsService(
            IOptions<SourcesConfiguration> config,
            IEventBuffer eventBuffer,
            ILogger<InternalMetricsService> logger)
            : base(config.Value.Internals, eventBuffer, nameof(InternalMetricsService), logger)
        {
            if (this.Enabled && this.Config.EnableMetrics)
            {
                this.meterListener = new MeterListener();
                this.meterListener.InstrumentPublished = OnInstrumentPublished;
                this.meterListener.SetMeasurementEventCallback<int>(OnMeasurement);
                this.meterListener.SetMeasurementEventCallback<long>(OnMeasurement);
                this.meterListener.SetMeasurementEventCallback<double>(OnMeasurement);
                this.meterListener.Start();
                InternalMetricsLog.MetricsListenerStarted(logger);
            }

            if (this.Enabled && this.Config.EnableTracing)
            {
                this.activityListener = new ActivityListener
                {
                    ShouldListenTo = source => source.Name.StartsWith("Logship.Agent", StringComparison.Ordinal),
                    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                    ActivityStopped = OnActivityStopped,
                };
                ActivitySource.AddActivityListener(this.activityListener);
                InternalMetricsLog.TracingListenerStarted(logger);
            }
        }

        private static void OnInstrumentPublished(Instrument instrument, MeterListener listener)
        {
            if (instrument.Meter.Name.StartsWith("Logship.Agent", StringComparison.Ordinal))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        }

        private void OnMeasurement<T>(Instrument instrument, T measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
            where T : struct
        {
            var now = DateTimeOffset.UtcNow;
            var data = new Dictionary<string, object>
            {
                { "machine", Environment.MachineName },
                { "meter", instrument.Meter.Name },
                { "instrument", instrument.Name },
                { "unit", instrument.Unit ?? string.Empty },
                { "value", Convert.ToDouble(measurement, System.Globalization.CultureInfo.InvariantCulture) },
            };

            foreach (var tag in tags)
            {
                if (tag.Value != null)
                {
                    data[$"tag_{tag.Key}"] = tag.Value;
                }
            }

            var record = new DataRecord("logship.agent.metrics", now, data);
            lock (metricsLock)
            {
                pendingMetrics.Add(record);
            }
        }

        private void OnActivityStopped(Activity activity)
        {
            var data = new Dictionary<string, object>
            {
                { "machine", Environment.MachineName },
                { "traceId", activity.TraceId.ToString() },
                { "spanId", activity.SpanId.ToString() },
                { "parentSpanId", activity.ParentSpanId.ToString() },
                { "operationName", activity.OperationName },
                { "displayName", activity.DisplayName },
                { "source", activity.Source.Name },
                { "kind", activity.Kind.ToString() },
                { "status", activity.Status.ToString() },
                { "durationMs", activity.Duration.TotalMilliseconds },
                { "startTime", activity.StartTimeUtc.ToString("O") },
            };

            if (activity.StatusDescription != null)
            {
                data["statusDescription"] = activity.StatusDescription;
            }

            foreach (var tag in activity.Tags)
            {
                if (tag.Value != null)
                {
                    data[$"tag_{tag.Key}"] = tag.Value;
                }
            }

            var record = new DataRecord("logship.agent.traces", DateTimeOffset.UtcNow, data);
            lock (tracesLock)
            {
                pendingTraces.Add(record);
            }
        }

        protected override Task ExecuteSingleAsync(CancellationToken token)
        {
            // Collect observable instruments (gauges)
            this.meterListener?.RecordObservableInstruments();

            // Drain pending metrics
            List<DataRecord> metrics;
            lock (metricsLock)
            {
                metrics = new List<DataRecord>(pendingMetrics);
                pendingMetrics.Clear();
            }

            if (metrics.Count > 0)
            {
                InternalMetricsLog.EmittingMetrics(this.Logger, metrics.Count);
                this.Buffer.Add(metrics);
            }

            // Drain pending traces
            List<DataRecord> traces;
            lock (tracesLock)
            {
                traces = new List<DataRecord>(pendingTraces);
                pendingTraces.Clear();
            }

            if (traces.Count > 0)
            {
                InternalMetricsLog.EmittingTraces(this.Logger, traces.Count);
                this.Buffer.Add(traces);
            }

            // Emit process metrics
            EmitProcessMetrics();

            return Task.CompletedTask;
        }

        private void EmitProcessMetrics()
        {
            var now = DateTimeOffset.UtcNow;
            var process = Process.GetCurrentProcess();
            var gcInfo = GC.GetGCMemoryInfo();

            var record = new DataRecord("logship.agent.process", now, new Dictionary<string, object>
            {
                { "machine", Environment.MachineName },
                { "workingSetBytes", process.WorkingSet64 },
                { "privateMemoryBytes", process.PrivateMemorySize64 },
                { "gcHeapSizeBytes", GC.GetTotalMemory(false) },
                { "gcTotalAllocatedBytes", GC.GetTotalAllocatedBytes(false) },
                { "gen0Collections", GC.CollectionCount(0) },
                { "gen1Collections", GC.CollectionCount(1) },
                { "gen2Collections", GC.CollectionCount(2) },
                { "threadCount", process.Threads.Count },
                { "handleCount", process.HandleCount },
                { "totalProcessorTimeMs", process.TotalProcessorTime.TotalMilliseconds },
                { "gcFragmentationBytes", gcInfo.FragmentedBytes },
            });

            this.Buffer.Add(record);
        }

        protected override Task OnStop(CancellationToken token)
        {
            this.meterListener?.Dispose();
            this.activityListener?.Dispose();
            return base.OnStop(token);
        }
    }

    internal static partial class InternalMetricsLog
    {
        [LoggerMessage(LogLevel.Information, "Internal metrics listener started")]
        public static partial void MetricsListenerStarted(ILogger logger);

        [LoggerMessage(LogLevel.Information, "Internal tracing listener started")]
        public static partial void TracingListenerStarted(ILogger logger);

        [LoggerMessage(LogLevel.Debug, "Emitting {Count} internal metric records")]
        public static partial void EmittingMetrics(ILogger logger, int count);

        [LoggerMessage(LogLevel.Debug, "Emitting {Count} internal trace records")]
        public static partial void EmittingTraces(ILogger logger, int count);
    }
}
