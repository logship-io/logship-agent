// <copyright file="SystemCpuService.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Globalization;

namespace Logship.Agent.Core.Services.Sources.Common
{
    internal sealed class SystemCpuService : BaseIntervalInputService<SystemCpuConfiguration>
    {
        private readonly Dictionary<string, CpuTimes> previousLinuxCpuTimes = new();
        private PerformanceCounter? windowsCpuCounter;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "Only populated and read behind OperatingSystem.IsWindows.")]
        private IReadOnlyList<PerformanceCounter> windowsCoreCounters = Array.Empty<PerformanceCounter>();

        public SystemCpuService(IOptions<SourcesConfiguration> config, IEventBuffer buffer, ILogger<SystemCpuService> logger)
            : base(config.Value.SystemCpu, buffer, nameof(SystemCpuService), logger)
        {
            if (this.Enabled && OperatingSystem.IsWindows())
            {
                try
                {
                    this.windowsCpuCounter = CreateWindowsCpuCounter();
                    this.windowsCoreCounters = CreateWindowsCoreCounters();
                }
                catch (Exception ex)
                {
                    SystemCpuServiceLog.WindowsCounterInitializationFailed(Logger, ex);
                    this.Enabled = false;
                }
            }
            else if (this.Enabled && false == OperatingSystem.IsLinux())
            {
                ServiceLog.SkipPlatformServiceExecution(Logger, nameof(SystemCpuService), Environment.OSVersion);
                this.Enabled = false;
            }
        }

        protected override Task ExecuteSingleAsync(CancellationToken token)
        {
            if (OperatingSystem.IsWindows())
            {
                this.EmitWindowsCpu();
            }
            else if (OperatingSystem.IsLinux())
            {
                this.EmitLinuxCpu();
            }

            return Task.CompletedTask;
        }

        protected override Task OnStop(CancellationToken token)
        {
            this.windowsCpuCounter?.Dispose();
            foreach (var counter in this.windowsCoreCounters)
            {
                counter.Dispose();
            }

            return base.OnStop(token);
        }

        private void EmitLinuxCpu()
        {
            foreach (var current in ReadLinuxCpuTimes())
            {
                // Prime value
                if (false == this.previousLinuxCpuTimes.TryGetValue(current.Name, out var previous))
                {
                    this.previousLinuxCpuTimes[current.Name] = current;
                    continue;
                }

                this.previousLinuxCpuTimes[current.Name] = current;
                var percentage = CalculateLinuxUsagePercentage(previous, current);
                if (percentage == null)
                {
                    continue;
                }

                var schema = current.IsAggregate ? "System.CPU" : "System.CPU.Core";
                var record = CreateRecord(schema);
                record.Data["cores"] = Environment.ProcessorCount;
                record.Data["percentage"] = percentage.Value;
                if (false == current.IsAggregate)
                {
                    record.Data["core"] = current.CoreIndex;
                }

                this.Buffer.Add(record);
            }
        }

        private static IReadOnlyList<CpuTimes> ReadLinuxCpuTimes()
        {
            var results = new List<CpuTimes>();
            foreach (var line in File.ReadLines("/proc/stat"))
            {
                if (false == line.StartsWith("cpu", StringComparison.Ordinal))
                {
                    break;
                }

                var split = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (split.Length < 5)
                {
                    throw new InvalidOperationException("A CPU line in /proc/stat is malformed.");
                }

                var name = split[0];
                if (name != "cpu" && false == int.TryParse(name.AsSpan(3), CultureInfo.InvariantCulture, out _))
                {
                    continue;
                }

                results.Add(item: new CpuTimes(
                    Name: name,
                    User: ParseLinuxCpuTick(split, 1),
                    Nice: ParseLinuxCpuTick(split, 2),
                    System: ParseLinuxCpuTick(split, 3),
                    Idle: ParseLinuxCpuTick(split, 4),
                    Iowait: ParseLinuxCpuTick(split, 5),
                    Irq: ParseLinuxCpuTick(split, 6),
                    Softirq: ParseLinuxCpuTick(split, 7),
                    Steal: ParseLinuxCpuTick(split, 8),
                    Guest: ParseLinuxCpuTick(split, 9),
                    GuestNice: ParseLinuxCpuTick(split, 10)));
            }

            if (results.Count == 0)
            {
                throw new InvalidOperationException("Unable to read CPU lines from /proc/stat.");
            }

            return results;
        }

        private static long ParseLinuxCpuTick(string[] split, int index)
        {
            if (index >= split.Length)
            {
                return 0;
            }

            return long.Parse(split[index], CultureInfo.InvariantCulture);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "Guarded by OperatingSystem.IsWindows.")]
        private static PerformanceCounter CreateWindowsCpuCounter()
        {
            var counter = new PerformanceCounter("Processor", "% Processor Time", "_Total", readOnly: true);
            _ = counter.NextValue();
            return counter;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "Guarded by OperatingSystem.IsWindows.")]
        private static IReadOnlyList<PerformanceCounter> CreateWindowsCoreCounters()
        {
            var category = new PerformanceCounterCategory("Processor");
            var counters = category.GetInstanceNames()
                .Where(static instance => int.TryParse(instance, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                .OrderBy(static instance => int.Parse(instance, CultureInfo.InvariantCulture))
                .Select(static instance =>
                {
                    var counter = new PerformanceCounter("Processor", "% Processor Time", instance, readOnly: true);
                    _ = counter.NextValue();
                    return counter;
                })
                .ToList();

            return counters;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "Guarded by OperatingSystem.IsWindows.")]
        private void EmitWindowsCpu()
        {
            if (this.windowsCpuCounter == null)
            {
                return;
            }

            var record = CreateRecord("System.CPU");
            record.Data["cores"] = Environment.ProcessorCount;
            record.Data["percentage"] = this.windowsCpuCounter.NextValue();
            this.Buffer.Add(record);

            foreach (var counter in this.windowsCoreCounters)
            {
                var coreRecord = CreateRecord("System.CPU.Core");
                coreRecord.Data["cores"] = Environment.ProcessorCount;
                coreRecord.Data["core"] = int.Parse(counter.InstanceName, CultureInfo.InvariantCulture);
                coreRecord.Data["percentage"] = counter.NextValue();
                this.Buffer.Add(coreRecord);
            }
        }

        private static double? CalculateLinuxUsagePercentage(CpuTimes previous, CpuTimes current)
        {
            var totalDelta = current.Total - previous.Total;
            if (totalDelta <= 0)
            {
                return null;
            }

            var idleDelta = current.IdleAll - previous.IdleAll;
            var activeDelta = Math.Max(0, totalDelta - idleDelta);
            return activeDelta * 100.0 / totalDelta;
        }

        private sealed record CpuTimes(
            string Name,
            long User,
            long Nice,
            long System,
            long Idle,
            long Iowait,
            long Irq,
            long Softirq,
            long Steal,
            long Guest,
            long GuestNice)
        {
            public bool IsAggregate => string.Equals(this.Name, "cpu", StringComparison.OrdinalIgnoreCase);

            public int CoreIndex => int.Parse(this.Name.AsSpan(3), CultureInfo.InvariantCulture);

            public long IdleAll => this.Idle + this.Iowait;

            public long Total => this.User + this.Nice + this.System + this.Idle + this.Iowait + this.Irq + this.Softirq + this.Steal + this.Guest + this.GuestNice;
        }
    }

    internal static partial class SystemCpuServiceLog
    {
        [LoggerMessage(LogLevel.Error, "Failed to initialize the Windows CPU performance counter.")]
        public static partial void WindowsCounterInitializationFailed(ILogger logger, Exception exception);
    }
}
