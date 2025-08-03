// <copyright file="ConsoleEventOutput.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Events;
using Logship.Agent.Core.Records;
using Microsoft.Extensions.Logging;

namespace Logship.Agent.Core
{
    internal sealed class ConsoleEventOutput : IEventOutput
    {
        private readonly ILogger<ConsoleEventOutput> logger;

        public ConsoleEventOutput(ILogger<ConsoleEventOutput> logger)
        {
            this.logger = logger;
        }

        public Task<bool> SendAsync(IReadOnlyCollection<DataRecord> records, CancellationToken cancellationToken)
        {
            var groups = records.GroupBy(_ => _.Schema);
            foreach (var group in groups)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var keys = string.Join(',', group.SelectMany(_ => _.Data.Keys).Distinct().Order());
                int count = group.Count();
                ConsoleEventLog.SendingRecords(this.logger, count, group.Key, keys);
            }

            return Task.FromResult(true);
        }
    }

    internal static partial class ConsoleEventLog
    {
        [LoggerMessage(LogLevel.Information, "Sending Record Group {Count} \"{Schema}\" with data keys: {Keys}")]
        public static partial void SendingRecords(ILogger logger, int count, string schema, string keys);
    }
}
