// <copyright file="SystemInformationService.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Logship.Agent.Core.Services.Sources.Common
{
    internal sealed class SystemInformationService : BaseIntervalInputService<SystemInformationConfiguration>
    {
        public SystemInformationService(IOptions<SourcesConfiguration> config, IEventBuffer buffer, ILogger<SystemInformationService> logger)
            : base(config.Value.SystemInformation, buffer, nameof(SystemInformationService), logger)
        {
        }

        protected override Task ExecuteSingleAsync(CancellationToken token)
        {
            var c = CreateRecord("System.Environment");
            c.Data["OSPlatform"] = Environment.OSVersion.Platform.ToString();
            c.Data["OSVersion"] = Environment.OSVersion.Version.ToString();
            c.Data["OSVersionString"] = Environment.OSVersion.VersionString;
            c.Data[nameof(Environment.ProcessorCount)] = Environment.ProcessorCount;
            c.Data[nameof(Environment.CommandLine)] = Environment.CommandLine;
            c.Data[nameof(Environment.TickCount64)] = Environment.TickCount64;
            c.Data[nameof(Environment.CurrentDirectory)] = Environment.CurrentDirectory;
            c.Data[nameof(Environment.StackTrace)] = Environment.StackTrace;
            c.Data[nameof(Environment.Version)] = Environment.Version.ToString();
            c.Data[nameof(Environment.UserDomainName)] = Environment.UserDomainName;
            c.Data[nameof(Environment.UserInteractive)] = Environment.UserInteractive;
            c.Data[nameof(Environment.UserName)] = Environment.UserName;
            c.Data[nameof(Environment.CurrentManagedThreadId)] = Environment.CurrentManagedThreadId;
            c.Data[nameof(Environment.ProcessId)] = Environment.ProcessId;
            c.Data[nameof(Environment.Is64BitOperatingSystem)] = Environment.Is64BitOperatingSystem;
            c.Data[nameof(Environment.Is64BitProcess)] = Environment.Is64BitProcess;
            c.Data[nameof(Environment.ProcessPath)] = Environment.ProcessPath ?? string.Empty;
            c.Data[nameof(Environment.SystemDirectory)] = Environment.SystemDirectory;
            c.Data[nameof(Environment.WorkingSet)] = Environment.WorkingSet;
            c.Data[nameof(Environment.SystemPageSize)] = Environment.SystemPageSize;
            Buffer.Add(c);
            return Task.CompletedTask;
        }
    }
}

