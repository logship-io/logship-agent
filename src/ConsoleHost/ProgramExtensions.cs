// <copyright file="ProgramExtensions.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Logship.Agent.ConsoleHost
{
    internal static partial class ProgramExtensions
    {
        [LoggerMessage(LogLevel.Information, "Cancellation key pressed. Beginning graceful application shutdown. Press again to force quit.")]
        public static partial void Log_CancelKeyPress(ILogger logger);

        [LoggerMessage(LogLevel.Warning, "Forcing application shutdown. Some resources may not get cleaned up.")]
        public static partial void Log_ForceExit(ILogger logger);

        [LoggerMessage(LogLevel.Critical, "Unhandled AppDomain Exception")]
        public static partial void Log_AppDomain_UnhandledException(ILogger logger, Exception ex);

        [LoggerMessage(LogLevel.Information, "Agent started. Startup duration was {durationMs}ms")]
        public static partial void Log_AgentStarted(ILogger logger, long durationMs);

        [LoggerMessage(LogLevel.Information, "Agent stopping. Total uptime was {durationMs}ms")]
        public static partial void Log_AgentStopping(ILogger logger, long durationMs);

        [LoggerMessage(LogLevel.Information, "Agent stopped. Shutdown duration was {durationMs}ms")]
        public static partial void Log_AgentStopped(ILogger logger, long durationMs);

        [LoggerMessage(LogLevel.Warning, "Agent stop cancelled after {durationMs}ms. Events may not have been flushed.")]
        public static partial void Log_AgentStopCancelled(ILogger logger, long durationMs);

        [LoggerMessage(LogLevel.Information, "Agent execution complete. Thanks for using logship")]
        public static partial void Log_AgentComplete(ILogger logger);
        
        [LoggerMessage(LogLevel.Error, "Application settings failed validation.")]
        public static partial void Log_ValidationFailed(ILogger logger, OptionsValidationException exception);

        [LoggerMessage(LogLevel.Debug, "Mapping GRPC Services for OTLP")]
        public static partial void Log_MapOtlpGrpc(ILogger logger);

    }
}

