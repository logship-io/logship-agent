using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Logship.Agent.Core.Records;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace Logship.Agent.Core.Services.Sources.Linux.Proc
{
    /// <summary>
    /// This service reads the `/proc/modules` file on Linux systems to gather information about loaded kernel modules.
    /// </summary>
    internal sealed class ProcModulesReaderService : BaseIntervalInputService<ProcModulesConfiguration>
    {
        private sealed record ProcModuelData(string ModuleName, int SizeBytes, int RefCount, string Dependencies, string State, ulong MemoryOffset);

        /// <summary>
        /// Initializes a new instance of the <see cref="ProcModulesReaderService"/> class.
        /// </summary>
        /// <param name="config">The configuration.</param>
        /// <param name="buffer">The event buffer.</param>
        /// <param name="logger">A logger to use.</param>
        public ProcModulesReaderService(IOptions<SourcesConfiguration> config, IEventBuffer buffer, ILogger<ProcFileReaderService> logger)
            : base(config.Value.ProcModules, buffer, nameof(ProcModulesReaderService), logger)
        {
            if (this.Enabled && false == OperatingSystem.IsLinux())
            {
                ServiceLog.SkipPlatformServiceExecution(Logger, nameof(ProcModulesReaderService), Environment.OSVersion);
                this.Enabled = false;
            }
        }

        protected override Task ExecuteSingleAsync(CancellationToken token)
        {
            this.ReadLoadedModules();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Reads the `/proc/modules` file and populates the event buffer with the loaded modules information.
        /// </summary>
        private void ReadLoadedModules()
        {
            var now = DateTimeOffset.UtcNow;
            var modulesFileContent = File.ReadLines("/proc/modules");

            /*
             * The format of the modules file is as follows.
             * 
             * Column 1: Module name
             * Column 2: Size in bytes
             * Column 3: Reference count
             * Column 4: Dependencies (common-separated list of module names or a '-' for none)
             * Column 5: State (e.g., "Live" or "Unloading")
             * Column 6: Memory offset (hexadecimal address where the module is loaded in memory, or "0x0000000000000000" if not applicable)
             * 
             *  Here's an example two lines from the file:
             * tls 98304 0 - Live 0x0000000000000000
             * intel_rapl_msr 16384 0 - Live 0x0000000000000000
             */

            foreach (var line in modulesFileContent)
            {
                var parsed = ReadLine(line, this.Logger);
                if (parsed == null)
                {
                    continue;
                }

                this.Buffer.Add(new DataRecord(
                            "System.Modules",
                            now,
                            new Dictionary<string, object>
                            {
                                    { "machine", Environment.MachineName },
                                    { "ModuleName", parsed.ModuleName },
                                    { "SizeBytes", parsed.SizeBytes },
                                    { "RefCount", parsed.RefCount },
                                    { "Dependencies", parsed.Dependencies },
                                    { "State", parsed.State },
                                    { "MemoryOffset", parsed.MemoryOffset }
                            }));
            }
        }

        /// <summary>
        /// Reads a single line from the `/proc/modules` file and parses it into a `ProcModuelData` object.
        /// </summary>
        /// <param name="line">A single line from the modules file</param>
        /// <param name="logger">The logger.</param>
        /// <returns>The parsed proc module file data or a null object on failure.</returns>
        private static ProcModuelData? ReadLine(string line, ILogger logger)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 6)
            {
                ProcModuleReaderServiceLog.LogInvalidModuleLine(logger, line);
                return null;
            }
            var moduleName = parts[0];

            if (false == int.TryParse(parts[1], CultureInfo.InvariantCulture, out var sizeBytes))
            {
                ProcModuleReaderServiceLog.LogInvalidModuleLine(logger, line);
                return null;
            }

            if (false == int.TryParse(parts[2], CultureInfo.InvariantCulture, out var refCount))
            {
                ProcModuleReaderServiceLog.LogInvalidModuleLine(logger, line);
                return null;
            }

            var dependencies = parts[3] == "-" ? string.Empty : parts[3];

            var state = parts[4];

            // We need to trim off the "0x" prefix from the memory offset.
            if (false == ulong.TryParse(parts[5].AsSpan().Slice(2), System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var memoryOffset))
            {
                ProcModuleReaderServiceLog.LogInvalidModuleLine(logger, line);
                return null;
            }
            return new ProcModuelData(moduleName, sizeBytes, refCount, dependencies, state, memoryOffset);
        }
    }

    internal static partial class ProcModuleReaderServiceLog
    {
        [LoggerMessage(LogLevel.Warning, "Invalid module info while processing module file: {Line}")]
        public static partial void LogInvalidModuleLine(ILogger logger, string line);
    }
}
