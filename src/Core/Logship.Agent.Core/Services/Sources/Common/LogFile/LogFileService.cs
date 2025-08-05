// <copyright file="LogFileService.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Logship.Agent.Core.Records;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace Logship.Agent.Core.Services.Sources.Common.LogFile
{
    public sealed class LogFileService : BaseInputService<LogFileServiceConfiguration>, IDisposable
    {
        private readonly ConcurrentDictionary<string, LogFileReader> _fileReaders;
        private readonly ConcurrentDictionary<string, DateTime> _lastFileStates;
        private readonly FileWatcher _fileWatcher;
        private readonly FileCheckpoint _checkpoint;

        protected override bool ExitOnException => false;

        public LogFileService(IOptions<SourcesConfiguration> config, IOptions<OutputConfiguration> outputConfig, IEventBuffer buffer, ILogger<LogFileService> logger)
            : base(config.Value.LogFile, buffer, nameof(LogFileService), logger)
        {
            _fileReaders = new ConcurrentDictionary<string, LogFileReader>();
            _lastFileStates = new ConcurrentDictionary<string, DateTime>();
            _fileWatcher = new FileWatcher(Config, logger);
            _checkpoint = new FileCheckpoint(Path.Join(outputConfig.Value.DataPath, "logfile-checkpoints"), logger);
        }

        protected override async Task ExecuteAsync(CancellationToken token)
        {
            if (Config.Include.Length == 0)
            {
                LogFileServiceLog.NoIncludePatterns(Logger);
                return;
            }

            LogFileServiceLog.StartingMonitoring(Logger, Config.Include.Length);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await ProcessFilesAsync(token);
                    await Task.Delay(Config.GlobMinimumCooldownMs, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogFileServiceLog.FileProcessingError(Logger, ex);
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                }
            }
        }

        public async Task ProcessFilesAsync(CancellationToken token)
        {
            if (_fileWatcher.ShouldRescanFiles())
            {
                var availableFiles = _fileWatcher.GetCurrentFiles().ToHashSet(StringComparer.OrdinalIgnoreCase);
                var filesToRead = _checkpoint.GetFilesToRead(availableFiles, Config.IgnoreCheckpoints).ToHashSet(StringComparer.OrdinalIgnoreCase);

                // Remove readers for files that no longer match or don't need reading
                var obsoleteFiles = _fileReaders.Keys.Except(filesToRead, StringComparer.OrdinalIgnoreCase).ToList();
                foreach (var obsoleteFile in obsoleteFiles)
                {
                    if (_fileReaders.TryRemove(obsoleteFile, out var reader))
                    {
                        reader.Dispose();
                    }
                    _lastFileStates.TryRemove(obsoleteFile, out _);
                }

                // Add readers for files that need reading
                foreach (var filePath in filesToRead)
                {
                    if (!_fileReaders.ContainsKey(filePath))
                    {
                        AddFileReader(filePath);
                    }
                }

                _fileWatcher.StartWatching(availableFiles, OnFileChanged);
            }

            // Process files that need reading
            var filesToProcess = _checkpoint.GetFilesToRead(_fileReaders.Keys, Config.IgnoreCheckpoints);
            var processingTasks = _fileReaders.Values
                .Where(reader => filesToProcess.Contains(reader.FilePath))
                .Select(reader => ProcessFileAsync(reader, token))
                .ToArray();
            
            if (processingTasks.Length > 0)
            {
                await Task.WhenAll(processingTasks);
            }
        }

        private void AddFileReader(string filePath)
        {
            try
            {
                var reader = new LogFileReader(filePath, Config, Logger);
                var fileInfo = new FileInfo(filePath);

                if (_checkpoint.ShouldResumeFromCheckpoint(filePath, Config.IgnoreCheckpoints))
                {
                    var position = _checkpoint.GetPosition(filePath);
                    reader.SetPosition(position);
                    LogFileServiceLog.ResumingFromCheckpoint(Logger, filePath, position);
                }
                else if (!Config.StartAtBeginning && fileInfo.Exists)
                {
                    // Start at end of file
                    reader.SetPosition(fileInfo.Length);
                }

                _fileReaders.TryAdd(filePath, reader);
                var initialLastModified = fileInfo.Exists ? fileInfo.LastWriteTimeUtc : DateTime.MinValue;
                _lastFileStates.TryAdd(filePath, initialLastModified);
            }
            catch (Exception ex)
            {
                LogFileServiceLog.FileReadError(Logger, filePath, 0, ex);
            }
        }

        private async Task ProcessFileAsync(LogFileReader reader, CancellationToken token)
        {
            try
            {
                var fileInfo = new FileInfo(reader.FilePath);
                if (!fileInfo.Exists)
                {
                    return;
                }

                var lastModified = _lastFileStates.GetValueOrDefault(reader.FilePath, DateTime.MinValue);
                if (fileInfo.LastWriteTimeUtc <= lastModified && reader.CurrentPosition >= fileInfo.Length)
                {
                    return;
                }

                var oldSize = reader.CurrentPosition;
                var newSize = fileInfo.Length;

                if (newSize < oldSize)
                {
                    // File was truncated or rotated
                    LogFileServiceLog.FileRotation(Logger, reader.FilePath, reader.FilePath);
                    reader.SetPosition(0);
                }
                else if (newSize > oldSize)
                {
                    LogFileServiceLog.FileSizeChanged(Logger, reader.FilePath, oldSize, newSize);
                }

                await foreach (var processedLine in reader.ReadLinesAsync(token))
                {
                    var record = CreateLogFileRecord(reader.FilePath, processedLine, fileInfo);
                    Buffer.Add(record);

                    // Update checkpoint (automatically persisted)
                    _checkpoint.SetPosition(reader.FilePath, reader.CurrentPosition);
                }

                _lastFileStates.AddOrUpdate(reader.FilePath, fileInfo.LastWriteTimeUtc, (_, _) => fileInfo.LastWriteTimeUtc);
            }
            catch (Exception ex)
            {
                LogFileServiceLog.FileReadError(Logger, reader.FilePath, reader.CurrentPosition, ex);
            }
        }

        private DataRecord CreateLogFileRecord(string filePath, ProcessedLine processedLine, FileInfo fileInfo)
        {
            var record = CreateRecord("LogFile");

            record.Data["FilePath"] = filePath;
            record.Data["Content"] = processedLine.Content;
            record.Data["LineNumber"] = processedLine.LineNumber;
            record.Data["ByteOffset"] = processedLine.ByteOffset;
            record.Data["FileSize"] = fileInfo.Length;
            record.Data["ModifiedTime"] = fileInfo.LastWriteTimeUtc;
            if (processedLine.MultilineId != null)
            {
                record.Data["MultilineId"] = processedLine.MultilineId;
                LogFileServiceLog.MultilineCompleted(Logger, 1, processedLine.MultilineId);
            }

            return record;
        }

        private void OnFileChanged(string directory, FileSystemEventArgs e)
        {
            if (e.FullPath != null)
            {
                // Notify checkpoint system of file change
                _checkpoint.OnFileChanged(e.FullPath);
                
                // If we have a reader for this file, potentially process it immediately
                if (_fileReaders.TryGetValue(e.FullPath, out var reader))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await ProcessFileAsync(reader, CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            LogFileServiceLog.FileProcessingError(Logger, ex);
                        }
                    });
                }
            }
        }

        protected override async Task OnStop(CancellationToken token)
        {
            Dispose();
            await base.OnStop(token);
        }

        public void Dispose()
        {
            foreach (var reader in _fileReaders.Values)
            {
                reader?.Dispose();
            }
            _fileReaders.Clear();

            _fileWatcher?.Dispose();
            _checkpoint?.Dispose();
        }
    }
}
