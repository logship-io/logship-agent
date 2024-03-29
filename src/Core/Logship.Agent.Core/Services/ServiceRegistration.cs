﻿using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Logship.Agent.Core.Inputs.Common;
using Logship.Agent.Core.Services.Sources.Common;
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
            return @this
                .AddHttpClient()
                .AddSingleton<IEventOutput>(_ =>
                {
                    var config = _.GetRequiredService<IOptions<OutputConfiguration>>();
                    if (config.Value.Endpoint.Equals(OutputConfiguration.CONSOLEOUTPUT, StringComparison.OrdinalIgnoreCase))
                    {
                        return new ConsoleEventOutput(_.GetRequiredService<ILogger<ConsoleEventOutput>>());
                    }

                    return new LogshipEventOutput(config, 
                        _.GetRequiredService<IHttpClientFactory>(),
                        _.GetRequiredService<ILogger<LogshipEventOutput>>());
                })
                .AddSingleton<IEventBuffer, InMemoryBuffer>()
                .AddSingleton<IEventSink, EventSink>()
                .AddHostedService<AgentHealthService>()
                .AddHostedService<AgentPushService>()
                .AddHostedService<DiskInformationService>()
                .AddHostedService<HealthChecksService>()
                .AddHostedService<JournalCtlService>()
                .AddHostedService<NetworkInformationService>()
                .AddHostedService<ProcMemReaderService>()
                .AddHostedService<ProcFileReaderService>()
                .AddHostedService<SystemInformationService>()
                .AddHostedService<SystemProcessInformationService>()
                .AddHostedService<UdpListenerService>()
                .AddHostedService<EtwService>()
                .AddHostedService<PerformanceCountersService>()
            ;
        }
    }
}
