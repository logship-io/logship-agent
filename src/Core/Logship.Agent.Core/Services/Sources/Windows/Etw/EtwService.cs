// <copyright file="EtwService.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Logship.Agent.Core.Services.Sources.Windows.Etw
{
    internal sealed class EtwService : BaseInputService<WindowsETWConfiguration>
    {
        private static readonly string SessionNamePrefix = $"Logship-Agent-{nameof(EtwService)}";

        private string sessionNamePrefix = SessionNamePrefix;
        private bool cleanupOldSessions;
        private bool reuseExistingSession;

        private ITraceEventSession? traceEventSession;

        public Func<ITraceEventSession> SessionFactory { get; set; }


        public EtwService(IOptions<SourcesConfiguration> config, IEventBuffer eventBuffer, ILogger<EtwService> logger)
            : base(config.Value.WindowsETW, eventBuffer, nameof(EtwService), logger)
        {
            this.cleanupOldSessions = this.Config.CleanupOldSessions;
            this.reuseExistingSession = this.Config.ReuseExistingSession;
            if (false == string.IsNullOrWhiteSpace(this.Config.SessionNamePrefix))
            {
                this.sessionNamePrefix = this.Config.SessionNamePrefix;
            }
            else
            {
                this.sessionNamePrefix = SessionNamePrefix;
            }

            SessionFactory = () => new StandardTraceEventSession(sessionNamePrefix, cleanupOldSessions, reuseExistingSession);
            if (this.Enabled && false == OperatingSystem.IsWindows())
            {
                ServiceLog.SkipPlatformServiceExecution(Logger, nameof(EtwService), Environment.OSVersion);
                this.Enabled = false;
            }
        }

        protected override Task ExecuteAsync(CancellationToken token)
        {
            if (this.Config.Providers.Count == 0)
            {
                return Task.CompletedTask;
            }

            traceEventSession = SessionFactory();
            foreach (var provider in this.Config.Providers)
            {
                if (provider.ProviderGuid != null && provider.ProviderGuid != Guid.Empty)
                {
                    EtwServiceLog.EnablingProvider(Logger, provider.ProviderGuid.Value);
                    traceEventSession.EnableProvider(provider.ProviderGuid.Value, provider.Level, (ulong)provider.Keywords);
                }
                else if (false == string.IsNullOrWhiteSpace(provider.ProviderName))
                {
                    EtwServiceLog.EnablingProvider(Logger, provider.ProviderName);
                    traceEventSession.EnableProvider(provider.ProviderName, provider.Level, (ulong)provider.Keywords);
                }
            }

            traceEventSession.EnableProvider(TraceEventExtensions.TplEventSourceGuid, TraceEventLevel.Always, TraceEventExtensions.TaskFlowActivityIdsKeyword);
            return Task.Run(() =>
            {
                try
                {
                    traceEventSession.Process(this.Buffer.Add, token);
                }
                catch (Exception e)
                {
                    EtwServiceLog.SessionError(Logger, e);
                }
            }, token);
        }

        protected override Task OnStop(CancellationToken token)
        {
            if (traceEventSession != null)
            {
                traceEventSession.Dispose();
                traceEventSession = null;
            }

            return base.OnStop(token);
        }

    }

    internal static partial class EtwServiceLog
    {
        [LoggerMessage(LogLevel.Information, "Enabling ETW Provider {ProviderName}")]
        public static partial void EnablingProvider(ILogger logger, string providerName);

        [LoggerMessage(LogLevel.Information, "Enabling ETW Provider {ProviderGuid}")]
        public static partial void EnablingProvider(ILogger logger, Guid providerGuid);

        [LoggerMessage(LogLevel.Error, "ETW session has terminated unexpectedly and events are no longer collected.")]
        public static partial void SessionError(ILogger logger, Exception exception);
    }
}

