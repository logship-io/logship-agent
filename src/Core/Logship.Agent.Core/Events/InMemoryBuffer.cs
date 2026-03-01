// <copyright file="InMemoryBuffer.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Records;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics.Metrics;

namespace Logship.Agent.Core.Events
{
    internal sealed class InMemoryBuffer : IEventBuffer
    {
        internal static readonly Meter BufferMeter = new("Logship.Agent.Buffer", "1.0.0");
        private static readonly Counter<long> RecordsAddedCounter = BufferMeter.CreateCounter<long>("logship.agent.buffer.records_added", "records", "Total records added to the buffer");
        private static readonly Counter<long> RecordsDroppedCounter = BufferMeter.CreateCounter<long>("logship.agent.buffer.records_dropped", "records", "Total records dropped due to buffer overflow");
        private static readonly Counter<long> RecordsFlushedCounter = BufferMeter.CreateCounter<long>("logship.agent.buffer.records_flushed", "records", "Total records retrieved from the buffer");

        private List<DataRecord> bag;
        private int maximumBufferSize;
        private readonly ILogger<InMemoryBuffer> logger;
        private readonly object mutex = new();

        const long OverflowWarnLogInterval = 5000;
        private long counter = OverflowWarnLogInterval;

        public InMemoryBuffer(IOptions<OutputConfiguration> outputConfig, ILogger<InMemoryBuffer> logger)
        {
            this.maximumBufferSize = outputConfig.Value.MaximumBufferSize;
            this.bag = new List<DataRecord>(maximumBufferSize);
            this.logger = logger;
            BufferMeter.CreateObservableGauge<int>("logship.agent.buffer.current_size", () => { lock (this.mutex) { return this.bag.Count; } }, "records", "Current number of records in the buffer");
            BufferMeter.CreateObservableGauge<int>("logship.agent.buffer.maximum_size", () => this.maximumBufferSize, "records", "Maximum buffer capacity");
            EventsLog.BufferSize(logger, this.maximumBufferSize);
        }

        public void Add(DataRecord data)
        {
            bool added = false;
            lock (mutex)
            {
                if (bag.Count < maximumBufferSize)
                {
                    bag.Add(DataRecord.SanitizeRecord(data));
                    added = true;
                }
            }

            if (added)
            {
                RecordsAddedCounter.Add(1);
            }
            else
            {
                RecordsDroppedCounter.Add(1);
                if (Interlocked.CompareExchange(ref counter, 0L, OverflowWarnLogInterval) == OverflowWarnLogInterval)
                {
                    MemoryBufferLog.WarnDataRecordDropped(this.logger, maximumBufferSize);
                }
                else
                {
                    Interlocked.Increment(ref counter);
                    MemoryBufferLog.TraceDataRecordDropped(this.logger, maximumBufferSize);
                }
            }
        }

        public void Add(IReadOnlyCollection<DataRecord> data)
        {
            int addedCount = 0;
            bool skipped = false;
            lock (mutex)
            {
                foreach (var item in data)
                {
                    if (bag.Count >= maximumBufferSize)
                    {
                        skipped = true;
                        break;
                    }

                    bag.Add(DataRecord.SanitizeRecord(item));
                    addedCount++;
                }
            }

            if (addedCount > 0)
            {
                RecordsAddedCounter.Add(addedCount);
            }

            if (skipped)
            {
                RecordsDroppedCounter.Add(data.Count - addedCount);
                if (Interlocked.CompareExchange(ref counter, 0L, OverflowWarnLogInterval) == OverflowWarnLogInterval)
                {
                    MemoryBufferLog.WarnDataRecordDropped(this.logger, maximumBufferSize);
                }
                else
                {
                    Interlocked.Increment(ref counter);
                    MemoryBufferLog.TraceDataRecordDropped(this.logger, maximumBufferSize);
                }
            }

        }

        public Task<IReadOnlyCollection<DataRecord>> NextAsync(CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return Task.FromResult<IReadOnlyCollection<DataRecord>>(Array.Empty<DataRecord>());
            }

            DataRecord[] items;
            lock (mutex)
            {
                items = this.bag.ToArray();
                this.bag.Clear();
            }

            if (items.Length > 0)
            {
                RecordsFlushedCounter.Add(items.Length);
            }

            return Task.FromResult<IReadOnlyCollection<DataRecord>>(items);
        }

        public bool BlockAdditions(bool block)
        {
            throw new NotImplementedException();
        }
    }

    internal static partial class MemoryBufferLog
    {
        [LoggerMessage(LogLevel.Warning, "Record dropped. Consider increasing maximumBufferSize: {MaximumBufferSize}")]
        public static partial void WarnDataRecordDropped(ILogger logger, int maximumBufferSize);

        [LoggerMessage(LogLevel.Trace, "Record dropped. Consider increasing maximumBufferSize: {MaximumBufferSize}")]
        public static partial void TraceDataRecordDropped(ILogger logger, int maximumBufferSize);
    }
}

