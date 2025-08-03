// <copyright file="ProcFileReaderService.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Logship.Agent.Core.Records;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Logship.Agent.Core.Services.Sources.Linux.Proc
{
    /// <summary>
    /// Reads open file info per process.
    /// </summary>
    internal sealed class ProcFileReaderService : BaseIntervalInputService<ProcOpenFilesConfiguration>
    {
        private readonly Dictionary<int, ProcPidData> processes;

        public ProcFileReaderService(IOptions<SourcesConfiguration> config, IEventBuffer buffer, ILogger<ProcFileReaderService> logger)
            : base(config.Value.ProcOpenFiles, buffer, nameof(ProcFileReaderService), logger)
        {
            processes = new Dictionary<int, ProcPidData>();
            if (this.Enabled && false == OperatingSystem.IsLinux())
            {
                ServiceLog.SkipPlatformServiceExecution(Logger, nameof(ProcFileReaderService), Environment.OSVersion);
                this.Enabled = false;
            }
        }

        protected override Task ExecuteSingleAsync(CancellationToken token)
        {
            ReadPerProcessFiles();
            return Task.CompletedTask;
        }

        private void ReadPerProcessFiles()
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var processDirectory in Directory.EnumerateDirectories("/proc"))
            {
                if (false == int.TryParse(Path.GetFileName(processDirectory), out var pid))
                {
                    continue;
                }

                ProcFileReaderServiceLog.ProcessLogged(Logger, processDirectory, Path.GetFileName(processDirectory));
                try
                {
                    // Fetch the process name.
                    var procName = File.ReadAllText(Path.Combine(processDirectory, "cmdline")).Split(" ")[0];

                    // Enumerate the open files.
                    foreach (var file in Directory.EnumerateFiles(Path.Combine(processDirectory, "fd")))
                    {
                        var info = new FileInfo(file);
                        if (false == info.Exists || null == info.LinkTarget)
                        {
                            continue;
                        }

                        var fileName = info.LinkTarget;

                        // If the file name ends with (deleted), then record that the file has been deleted, and strip the suffix.
                        var isDeleted = false;
                        if (fileName.EndsWith(" (deleted)", StringComparison.Ordinal))
                        {
                            isDeleted = true;
                            fileName = file.Substring(0, file.Length - 10);
                        }


                        this.Buffer.Add(new DataRecord(
                            "System.Process.Files",
                            now,
                            new Dictionary<string, object>
                            {
                                    { "machine", Environment.MachineName },
                                    { "processId", pid },
                                    { "executable", procName },
                                    { "file", fileName },
                                    { "deleted", isDeleted },
                            }));
                    }
                }
                catch (Exception ex)
                {
                    ProcFileReaderServiceLog.ProcPidError(Logger, pid, ex);
                }
            }
        }
    }

    internal static partial class ProcFileReaderServiceLog
    {
        [LoggerMessage(LogLevel.Trace, "Logging files for process: {ProcessId} {Directory}")]
        public static partial void ProcessLogged(ILogger logger, string processId, string directory);

        [LoggerMessage(LogLevel.Error, "Failed to log info for process {ProcessId}")]
        public static partial void ProcPidError(ILogger logger, int processId, Exception exception);
    }
}

