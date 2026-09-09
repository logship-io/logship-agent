// <copyright file="ServiceRegistration.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Logship.Agent.Core.Inputs.Common;
using Logship.Agent.Core.Internals;
using Logship.Agent.Core.Services.Sources.Common;
using Logship.Agent.Core.Services.Sources.Common.LogFile;
using Logship.Agent.Core.Services.Sources.Common.MQTT;
using Logship.Agent.Core.Services.Sources.Common.Nmap;
using Logship.Agent.Core.Services.Sources.Common.Otlp;
using Logship.Agent.Core.Services.Sources.Common.Prometheus;
using Logship.Agent.Core.Services.Sources.Common.Udp;
using Logship.Agent.Core.Services.Sources.Linux.JournalCtl;
using Logship.Agent.Core.Services.Sources.Linux.Proc;
using Logship.Agent.Core.Services.Sources.Linux.Security;
using Logship.Agent.Core.Services.Sources.Linux.Syslog;
using Logship.Agent.Core.Services.Sources.Windows.Etw;
using Microsoft.Extensions.DependencyInjection;

namespace Logship.Agent.Core.Services
{
    public static class ServiceRegistration
    {
        public static IServiceCollection AddAgentServices(this IServiceCollection @this)
        {
            @this
                .AddHttpClient()
                .AddSingleton<ITokenStorage, LocalStorage>()
                .AddSingleton<OutputAuthenticator>()
                .AddTransient<IOutputAuth>(_ => _.GetRequiredService<OutputAuthenticator>())
                .AddTransient<IRefreshAuth>(_ => _.GetRequiredService<OutputAuthenticator>())
                .AddSingleton<IEventOutput, LogshipEventOutput>()
                .AddSingleton<IReadOnlyDictionary<string, ExtractResourceAttributeValue>>(_ =>
                {
                    return new Dictionary<string, ExtractResourceAttributeValue>()
                    {
                        ["ServiceVersion"] = new ExtractResourceAttributeValue("service.version", string.Empty),
                        ["ServiceName"] = new ExtractResourceAttributeValue("service.name", "unknown_service"),
                    };
                })
                .AddSingleton<IEventBuffer, InMemoryBuffer>()
                .AddSingleton<IEventSink, EventSink>()
                .AddTransient<AgentAuthenticationService>()
                .AddHostedService<AgentHealthService>()
                .AddHostedService<AgentPushService>()
                .AddHostedService<DiskInformationService>()
                .AddHostedService<HealthChecksService>()
                .AddHostedService<JournalCtlService>()
                .AddHostedService<NetworkInformationService>()
                .AddHostedService<ProcMemReaderService>()
                .AddHostedService<ProcFileReaderService>()
                .AddHostedService<ProcModulesReaderService>()
                .AddHostedService<SystemCpuService>()
                .AddHostedService<SystemInformationService>()
                .AddHostedService<SystemProcessInformationService>()
                .AddHostedService<EtwService>()
                .AddHostedService<PerformanceCountersService>()
                .AddHostedService<UdpListenerService>()
                .AddHostedService<LogFileService>()
                .AddHostedService<NmapNetworkScannerService>()
                .AddHostedService<MQTTListenerService>()
                .AddHostedService<SyslogTcpReceiverService>()
                .AddHostedService<InternalMetricsService>()
                .AddHostedService<LinuxAuthEventsService>()
                .AddHostedService<LinuxSshPostureService>()
                .AddHostedService<LinuxPersistenceInventoryService>()
                .AddHostedService<LinuxDockerService>()
                .AddHostedService<PrometheusMetricsService>()
            ;

            return @this;
        }
    }
}

