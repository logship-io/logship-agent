// <copyright file="EventSink.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Internals;
using Logship.Agent.Core.Records;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Logship.Agent.Core.Events
{
    internal sealed class EventSink : IEventSink, IEventBuffer, IEventOutput, IDisposable
    {
        internal static readonly Meter SinkMeter = new("Logship.Agent.Sink", "1.0.0");
        internal static readonly ActivitySource SinkActivitySource = new("Logship.Agent.Sink", "1.0.0");
        private static readonly Counter<long> FlushCounter = SinkMeter.CreateCounter<long>("logship.agent.sink.flush_count", "flushes", "Total flush operations");
        private static readonly Counter<long> FlushSuccessCounter = SinkMeter.CreateCounter<long>("logship.agent.sink.flush_success", "flushes", "Successful flush operations");
        private static readonly Counter<long> FlushFailureCounter = SinkMeter.CreateCounter<long>("logship.agent.sink.flush_failure", "flushes", "Failed flush operations");
        private static readonly Histogram<double> FlushDuration = SinkMeter.CreateHistogram<double>("logship.agent.sink.flush_duration", "ms", "Duration of flush operations in milliseconds");

        private readonly int maximumFlushSize;
        private readonly IEventBuffer buffer;
        private readonly ILogger logger;
        private readonly IEventOutput eventOutput;
        private bool disposedValue;

        public EventSink(IOptions<OutputConfiguration> config, IEventOutput eventOutput, IEventBuffer buffer, ILogger<EventSink> logger)
        {
            this.maximumFlushSize = config.Value.MaximumFlushSize;
            this.buffer = buffer;
            this.logger = Throw.IfArgumentNull(logger, nameof(logger));
            this.eventOutput = Throw.IfArgumentNull(eventOutput, nameof(eventOutput));
        }

        public async Task FlushAsync(CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            IReadOnlyCollection<DataRecord> records = await this.buffer.NextAsync(token);
            if (records.Count == 0)
            {
                return;
            }

            using var activity = SinkActivitySource.StartActivity("Flush", ActivityKind.Internal);
            activity?.SetTag("logship.flush.record_count", records.Count);
            var sw = Stopwatch.StartNew();

            EventSinkLog.FlushingRecords(this.logger, records.Count);
            try
            {
                foreach (var batch in records.Chunk(this.maximumFlushSize))
                {
                    FlushCounter.Add(1);
                    using var flush = new EventSinkFlushContext(batch, onFailure: this.buffer.Add, logger);
                    flush.Success = await this.eventOutput.SendAsync(batch, token);
                    if (flush.Success)
                    {
                        FlushSuccessCounter.Add(1);
                    }
                    else
                    {
                        FlushFailureCounter.Add(1);
                        activity?.SetStatus(ActivityStatusCode.Error, "Flush failed");
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { /* noop */ }
            catch (Exception ex)
            {
                FlushFailureCounter.Add(1);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                EventSinkLog.Exception(this.logger, ex);
                throw;
            }
            finally
            {
                sw.Stop();
                FlushDuration.Record(sw.Elapsed.TotalMilliseconds);
                activity?.SetTag("logship.flush.duration_ms", sw.Elapsed.TotalMilliseconds);
            }
        }

        sealed class EventSinkFlushContext : IDisposable
        {
            private readonly ILogger logger;
            private readonly Action<IReadOnlyCollection<DataRecord>> onFailure;
            private readonly IReadOnlyCollection<DataRecord> records;
            private bool disposedValue;

            /// <summary>
            /// Whether this flush was successful.
            /// </summary>
            public bool Success { get; set; }

            internal EventSinkFlushContext(IReadOnlyCollection<DataRecord> records, Action<IReadOnlyCollection<DataRecord>> onFailure, ILogger logger)
            {
                this.records = records;
                this.onFailure = onFailure;
                this.logger = logger;
                this.Success = false;
            }

            private void Dispose(bool disposing)
            {
                if (!disposedValue)
                {
                    if (disposing)
                    {
                        if (false == this.Success)
                        {
                            EventSinkLog.FlushFailed(this.logger, records.Count);
                            this.onFailure(this.records);
                        }
                    }

                    disposedValue = true;
                }
            }

            public void Dispose()
            {
                // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
                Dispose(disposing: true);
                GC.SuppressFinalize(this);
            }
        }

        private void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    if (this.eventOutput is IDisposable eventD)
                    {
                        eventD.Dispose();
                    }

                    if (this.buffer is IDisposable bufferD)
                    {
                        bufferD.Dispose();
                    }
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        public Task<bool> SendAsync(IReadOnlyCollection<DataRecord> records, CancellationToken cancellationToken)
        {
            return eventOutput.SendAsync(records, cancellationToken);
        }

        public void Add(DataRecord data)
        {
            buffer.Add(data);
        }

        public void Add(IReadOnlyCollection<DataRecord> data)
        {
            buffer.Add(data);
        }

        public Task<IReadOnlyCollection<DataRecord>> NextAsync(CancellationToken token)
        {
            return buffer.NextAsync(token);
        }
    }

    internal static partial class EventSinkLog
    {
        [LoggerMessage(LogLevel.Information, "Flushing {FlushSize} data records.")]
        public static partial void FlushingRecords(ILogger logger, int flushSize);

        [LoggerMessage(LogLevel.Error, "An exception occurred during flush")]
        public static partial void Exception(ILogger logger, Exception exception);

        [LoggerMessage(LogLevel.Warning, "EventSink flush failed. Re-inserting {Count} records.")]
        public static partial void FlushFailed(ILogger logger, int count);
    }
}

