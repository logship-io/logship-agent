// <copyright file="InMemoryBuffer.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Records;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Logship.Agent.Core.Events
{
    internal sealed class InMemoryBuffer : IEventBuffer
    {
        private List<DataRecord> bag;
        private int maximumBufferSize;
        private readonly ILogger<InMemoryBuffer> logger;
        private readonly object mutex = new();

        const long OverflowWarnLogInterval = 5000;
        private long counter = OverflowWarnLogInterval;

        public InMemoryBuffer(IOptions<OutputConfiguration> outputConfig, ILogger<InMemoryBuffer> logger)
        {
            this.bag = new List<DataRecord>(maximumBufferSize);
            this.maximumBufferSize = outputConfig.Value.MaximumBufferSize;
            this.logger = logger;
            EventsLog.BufferSize(logger, this.maximumBufferSize);
        }

        public void Add(DataRecord data)
        {
            bool added = false;
            lock (mutex) 
            {
                if (bag.Count < maximumBufferSize)
                {
                    bag.Add(data);
                    added = true;
                }
            }

            if (false == added)
            {
                if(Interlocked.CompareExchange(ref counter, 0L, OverflowWarnLogInterval) == OverflowWarnLogInterval)
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
            foreach (var item in data)
            {
                bag.Add(item);
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

