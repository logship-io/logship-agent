// <copyright file="LinuxPersistenceInventoryService.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Logship.Agent.Core.Services.Sources.Linux.Security
{
    /// <summary>
    /// Inventories common Linux persistence locations used by services, scheduled jobs, and shell startup.
    /// </summary>
    /// <remarks>
    /// Configure with <c>Sources:Linux.PersistenceInventory</c>. <c>systemdDirectories</c> controls where
    /// <c>*.service</c> and <c>*.timer</c> files are discovered, <c>cronDirectories</c> controls cron
    /// inventory roots, <c>profilePaths</c> controls shell startup files/directories, and
    /// <c>rcLocalPath</c> controls the legacy rc.local path. <c>interval</c> controls how often the snapshot
    /// runs. The service emits <c>Linux.Persistence.Item</c> records with file metadata and systemd
    /// enabled hints based on <c>.wants</c>/<c>.requires</c> paths. It does not execute unit files, scripts,
    /// or scheduled jobs.
    /// </remarks>
    internal sealed class LinuxPersistenceInventoryService : BaseIntervalInputService<LinuxPersistenceInventoryConfiguration>
    {
        public LinuxPersistenceInventoryService(IOptions<SourcesConfiguration> config, IEventBuffer buffer, ILogger<LinuxPersistenceInventoryService> logger)
            : base(config.Value.LinuxPersistenceInventory, buffer, nameof(LinuxPersistenceInventoryService), logger)
        {
            if (this.Enabled && false == OperatingSystem.IsLinux())
            {
                ServiceLog.SkipPlatformServiceExecution(Logger, nameof(LinuxPersistenceInventoryService), Environment.OSVersion);
                this.Enabled = false;
            }
        }

        protected override bool ExitOnException => false;

        protected override Task ExecuteSingleAsync(CancellationToken token)
        {
            foreach (var directory in this.Config.SystemdDirectories)
            {
                this.EmitDirectoryItems(directory, "systemd", "*.service", token);
                this.EmitDirectoryItems(directory, "systemd_timer", "*.timer", token);
            }

            foreach (var directory in this.Config.CronDirectories)
            {
                this.EmitDirectoryItems(directory, "cron", "*", token);
            }

            foreach (var path in this.Config.ProfilePaths)
            {
                this.EmitPath(path, "shell_profile", token);
            }

            this.EmitPath(this.Config.RcLocalPath, "rc_local", token);
            return Task.CompletedTask;
        }

        private void EmitDirectoryItems(string directory, string type, string pattern, CancellationToken token)
        {
            if (token.IsCancellationRequested || false == Directory.Exists(directory))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories))
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                this.EmitPath(file, type, token);
            }
        }

        private void EmitPath(string path, string type, CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly))
                {
                    this.EmitFile(file, type);
                }
            }
            else
            {
                this.EmitFile(path, type);
            }
        }

        private void EmitFile(string path, string type)
        {
            var fileInfo = new FileInfo(path);
            if (false == fileInfo.Exists)
            {
                return;
            }

            var record = CreateRecord("Linux.Persistence.Item");
            record.Data["Type"] = type;
            LinuxFileMetadata.AddFileInfo(record.Data, fileInfo);

            if (type.StartsWith("systemd", StringComparison.Ordinal))
            {
                record.Data["EnabledHint"] = IsSystemdEnabledHint(fileInfo);
                record.Data["UnitName"] = fileInfo.Name;
            }

            this.Buffer.Add(record);
        }

        private static bool IsSystemdEnabledHint(FileInfo unitFile)
        {
            var directory = unitFile.Directory;
            while (directory != null)
            {
                if (directory.Name.EndsWith(".wants", StringComparison.Ordinal)
                    || directory.Name.EndsWith(".requires", StringComparison.Ordinal))
                {
                    return true;
                }

                directory = directory.Parent;
            }

            return false;
        }
    }
}
