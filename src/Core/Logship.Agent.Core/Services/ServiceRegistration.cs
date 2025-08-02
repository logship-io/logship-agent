// <copyright file="ServiceRegistration.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Logship.Agent.Core.Inputs.Common;
using Logship.Agent.Core.Internals;
using Logship.Agent.Core.Services.Sources.Common;
using Logship.Agent.Core.Services.Sources.Common.LogFile;
using Logship.Agent.Core.Services.Sources.Common.Otlp;
using Logship.Agent.Core.Services.Sources.Common.Udp;
using Logship.Agent.Core.Services.Sources.Linux.JournalCtl;
using Logship.Agent.Core.Services.Sources.Linux.Proc;
using Logship.Agent.Core.Services.Sources.Windows.Etw;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
                .AddTransient<IHandshakeAuth>(_ => _.GetRequiredService<OutputAuthenticator>())
                .AddSingleton<IEventOutput>(_ =>
                {
                    var config = _.GetRequiredService<IOptions<OutputConfiguration>>();
                    if (config.Value.Endpoint.Equals(OutputConfiguration.CONSOLEOUTPUT, StringComparison.OrdinalIgnoreCase))
                    {
                        return new ConsoleEventOutput(_.GetRequiredService<ILogger<ConsoleEventOutput>>());
                    }

                    return new LogshipEventOutput(config,
                        _.GetRequiredService<OutputAuthenticator>(),
                        _.GetRequiredService<IHttpClientFactory>(),
                        _.GetRequiredService<ILogger<LogshipEventOutput>>());
                })
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
                .AddTransient<AgentHandshakeService>()
                .AddHostedService<AgentHealthService>()
                .AddHostedService<AgentPushService>()
                .AddHostedService<DiskInformationService>()
                .AddHostedService<HealthChecksService>()
                .AddHostedService<JournalCtlService>()
                .AddHostedService<NetworkInformationService>()
                .AddHostedService<ProcMemReaderService>()
                .AddHostedService<ProcFileReaderService>()
                .AddHostedService<ProcModulesReaderService>()
                .AddHostedService<SystemInformationService>()
                .AddHostedService<SystemProcessInformationService>()
                .AddHostedService<EtwService>()
                .AddHostedService<PerformanceCountersService>()
                .AddHostedService<UdpListenerService>()
                .AddHostedService<LogFileService>()
            ;

            return @this;
        }
    }
}

