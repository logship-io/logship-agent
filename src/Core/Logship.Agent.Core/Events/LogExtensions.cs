// <copyright file="LogExtensions.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Microsoft.Extensions.Logging;

namespace Logship.Agent.Core.Events
{
    internal static partial class EventsLog
    {
        [LoggerMessage(LogLevel.Information, "Sending events to output {Endpoint} using account {Account}")]
        public static partial void Endpoint(ILogger logger, string endpoint, Guid account);

        [LoggerMessage(LogLevel.Information, "Maximum buffer size: {maximumBufferSize}")]
        public static partial void BufferSize(ILogger logger, int maximumBufferSize);
    }
}

