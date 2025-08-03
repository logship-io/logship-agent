// <copyright file="ProcMemReaderService.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Logship.Agent.Core.Records;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Globalization;

namespace Logship.Agent.Core.Services.Sources.Linux.Proc
{
    internal sealed class ProcMemReaderService : BaseIntervalInputService<ProcConfiguration>
    {
        private readonly Dictionary<int, ProcPidData> processes;
        private int ClockTicks;

        public ProcMemReaderService(IOptions<SourcesConfiguration> config, IEventBuffer buffer, ILogger<ProcMemReaderService> logger)
            : base(config.Value.Proc, buffer, nameof(ProcMemReaderService), logger)
        {
            processes = new Dictionary<int, ProcPidData>();
            if (this.Enabled && false == OperatingSystem.IsLinux())
            {
                ServiceLog.SkipPlatformServiceExecution(Logger, nameof(ProcMemReaderService), Environment.OSVersion);
                this.Enabled = false;
            }
        }

        protected override async Task ExecuteAsync(CancellationToken token)
        {
            this.ClockTicks = int.Parse(await ProcHelpers.ExecuteLinuxCommand("getconf", "CLK_TCK", token), CultureInfo.InvariantCulture);
            await base.ExecuteAsync(token);
        }

        protected override Task ExecuteSingleAsync(CancellationToken token)
        {
            ReadProcStat();
            ReadPerProcessStat(this.ClockTicks);
            return Task.CompletedTask;
        }

        private void ReadPerProcessStat(int tickRequency)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var processDirectory in Directory.EnumerateDirectories("/proc"))
            {
                if (false == int.TryParse(Path.GetFileName(processDirectory), out var pid))
                {
                    continue;
                }

                ProcMemReaderServiceLog.ProcessLogged(Logger, processDirectory, Path.GetFileName(processDirectory));
                try
                {
                    var procName = File.ReadAllText(Path.Combine(processDirectory, "cmdline")).Split(" ")[0];

                    var line = File.ReadAllText(Path.Combine(processDirectory, "stat"));
                    var split = line.Split(" ", StringSplitOptions.RemoveEmptyEntries);

                    var userTime = int.Parse(split[13], CultureInfo.InvariantCulture);
                    var kernalTIme = int.Parse(split[14], CultureInfo.InvariantCulture);

                    var numThreads = int.Parse(split[19], CultureInfo.InvariantCulture);

                    var rssSizePages = long.Parse(split[23], CultureInfo.InvariantCulture);

                    this.Buffer.Add(new DataRecord(
                            "System.Process.Threads",
                            now,
                            new Dictionary<string, object>
                            {
                                    { "machine", Environment.MachineName },
                                    { "processId", pid },
                                    { "executable", procName },
                                    { "count", numThreads }
                            }));

                    this.Buffer.Add(new DataRecord(
                            "System.Process.Memory",
                            now,
                            new Dictionary<string, object>
                            {
                                    { "machine", Environment.MachineName },
                                    { "processId", pid },
                                    { "executable", procName },
                                    { "bytes", rssSizePages * 4 * 1024 } // Convert to bytes.
                            }));

                    if (false == processes.TryGetValue(pid, out var data))
                    {
                        data = new ProcPidData(Stopwatch.StartNew(), procName, pid, userTime, kernalTIme, numThreads);
                        processes.Add(pid, data);
                    }
                    else
                    {
                        var timeDiff = data.watch.Elapsed;
                        var totalTicks = timeDiff.TotalSeconds * tickRequency;
                        var percentCpu = (userTime + kernalTIme - data.TotalTicks) / (double)totalTicks * 100.0;

                        this.Buffer.Add(new DataRecord(
                            "System.Process.Cpu",
                            now,
                            new Dictionary<string, object>
                            {
                                    { "machine", Environment.MachineName },
                                    { "processId", pid },
                                    { "executable", procName },
                                    { "percentage", percentCpu }
                            }));

                        processes[pid] = data with
                        {
                            watch = Stopwatch.StartNew(),
                            kernalTime = kernalTIme,
                            userTime = userTime,
                            numThreads = numThreads,
                        };
                    }
                }
                catch (Exception ex)
                {
                    ProcMemReaderServiceLog.ProcPidError(Logger, pid, ex);
                }
            }
        }

        private void ReadProcStat()
        {
            try
            {
                var now = DateTime.UtcNow;
                var recordLinux = new DataRecord(
                    "Linux.Proc.Stat",
                    now,
                    new Dictionary<string, object>
                    {
                            { "machine", Environment.MachineName },
                    });

                var recordSystem = new DataRecord(
                    "System.Memory",
                    now,
                    new Dictionary<string, object>
                    {
                            { "machine", Environment.MachineName },
                    });

                using (var readerStream = File.OpenRead("/proc/meminfo"))
                using (var reader = new StreamReader(readerStream))
                {
                    while (true)
                    {
                        var line = reader.ReadLine();
                        if (string.IsNullOrEmpty(line))
                        {
                            break;
                        }
                        var split = line.Split(" ", StringSplitOptions.RemoveEmptyEntries);
                        var name = split[0].Trim(':');
                        var size = long.Parse(split[1], CultureInfo.InvariantCulture);
                        var multiplier = 1L;
                        if (split.Length > 2)
                        {
                            var sizeStr = split[2];
                            switch (sizeStr)
                            {
                                case "kB":
                                    multiplier = 1024L;
                                    break;
                                case "mB":
                                    multiplier = 1024L * 1024;
                                    break;
                                case "gB":
                                    multiplier = 1024L * 1024 * 1024;
                                    break;
                                default:
                                    ProcMemReaderServiceLog.UnknownMultiplier(Logger, sizeStr);
                                    break;
                            }
                        }

                        size *= multiplier;
                        recordLinux.Data.Add(name.Replace("(", "_").Replace(")", "_"), size);

                        switch (name)
                        {
                            case "MemTotal":
                                recordSystem.Data.Add("TotalMemory", size);
                                break;
                            case "MemFree":
                                recordSystem.Data.Add("FreeMemory", size);
                                break;
                        }
                    }

                }

                this.Buffer.Add(recordSystem);
                this.Buffer.Add(recordLinux);
            }
            catch (Exception ex)
            {
                ProcMemReaderServiceLog.ProcStatException(Logger, ex);
            }
        }
    }

    internal static partial class ProcMemReaderServiceLog
    {
        [LoggerMessage(LogLevel.Trace, "Logging for process {ProcessId} {Directory}")]
        public static partial void ProcessLogged(ILogger logger, string processId, string directory);

        [LoggerMessage(LogLevel.Error, "Failed to read /proc/{ProcessID}/stat")]
        public static partial void ProcPidError(ILogger logger, int processId, Exception exception);

        [LoggerMessage(LogLevel.Error, "Unknown multiplier: {Multiplier}")]
        public static partial void UnknownMultiplier(ILogger logger, string multiplier);

        [LoggerMessage(LogLevel.Error, "Failed to read /proc/stat.")]
        public static partial void ProcStatException(ILogger logger, Exception exception);
    }
}

