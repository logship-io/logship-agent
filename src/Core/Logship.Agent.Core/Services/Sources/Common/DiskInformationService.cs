// <copyright file="DiskInformationService.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Logship.Agent.Core.Services.Sources.Common
{
    /// <summary>
    /// The disk info service emits disk statistics.
    /// </summary>
    internal sealed class DiskInformationService : BaseIntervalInputService<DiskInformationConfiguration>
    {
        public DiskInformationService(IOptions<SourcesConfiguration> config, IEventBuffer eventBuffer, ILogger<DiskInformationService> logger)
            : base(config.Value.DiskInfo, eventBuffer, nameof(DiskInformationService), logger)
        {
        }

        protected override Task ExecuteSingleAsync(CancellationToken token)
        {
            var drives = DriveInfo.GetDrives();

            var now = DateTimeOffset.UtcNow;
            foreach (var drive in drives)
            {
                DiskInfoLog.FoundDrive(this.Logger, drive.Name, drive.DriveType);
                var record = CreateRecord("System.Storage", now);
                record.Data.Add("Name", drive.Name);
                record.Data.Add("Type", drive.DriveType.ToString());
                record.Data.Add("RootDir", drive.RootDirectory.FullName);
                
                try
                {
                    record.Data.Add("TotalSizeBytes", drive.TotalSize);
                    record.Data.Add("TotalFreespaceBytes", drive.TotalFreeSpace);
                    record.Data.Add("AvailableFreespaceBytes", drive.AvailableFreeSpace);
                    record.Data.Add("VolumeLabel", drive.VolumeLabel);
                    record.Data.Add("Format", drive.DriveFormat);
                }
                catch (IOException ex)
                {
                    DiskInfoLog.ReadError(this.Logger, drive.Name, ex);
                }

                this.Buffer.Add(record);
            }

            return Task.CompletedTask;
        }
    }

    internal static partial class DiskInfoLog
    {
        [LoggerMessage(LogLevel.Error, "Error reading drive {DriveName}")]
        public static partial void ReadError(ILogger logger, string driveName, IOException exception);

        [LoggerMessage(LogLevel.Trace, "Found drive: {DriveName} - {DriveType}")]
        public static partial void FoundDrive(ILogger logger, string driveName, DriveType driveType);
    }
}

