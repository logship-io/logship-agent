// <copyright file="FileCheckpoint.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Logship.Agent.Core.Services.Sources.Common.LogFile
{
    internal sealed class FileCheckpoint : IDisposable
    {
        private readonly string _dataDirectory;
        private readonly ConcurrentDictionary<string, CheckpointInfo> _checkpoints;
        private readonly ILogger _logger;
        private readonly object _mutex = new object();

        private volatile bool _disposed;

        public FileCheckpoint(string dataDirectory, ILogger logger)
        {
            _dataDirectory = dataDirectory ?? Path.Combine(Path.GetTempPath(), "logship-checkpoints");
            _checkpoints = new ConcurrentDictionary<string, CheckpointInfo>();
            _logger = logger;

            Directory.CreateDirectory(_dataDirectory);
            LoadCheckpoints();
        }

        public long GetPosition(string filePath)
        {
            var normalizedPath = NormalizePath(filePath);
            var fileId = GetFileId(normalizedPath);
            return _checkpoints.TryGetValue(fileId, out var checkpoint) ? checkpoint.Position : 0;
        }

        public void SetPosition(string filePath, long position)
        {
            var normalizedPath = NormalizePath(filePath);
            var fileId = GetFileId(normalizedPath);
            var fileInfo = new FileInfo(normalizedPath);

            _checkpoints.AddOrUpdate(fileId, new CheckpointInfo(normalizedPath, position, fileInfo.Length, fileInfo.LastWriteTimeUtc),
                (_, existing) => existing with { Position = position, FileSize = fileInfo.Length, LastModified = fileInfo.LastWriteTimeUtc });

            // Immediately persist this checkpoint
            PersistSingleCheckpoint(fileId, _checkpoints[fileId]);
        }

        public IEnumerable<string> GetFilesToRead(IEnumerable<string> availableFiles, bool ignoreCheckpoints)
        {
            var filesToRead = new List<string>();

            foreach (var filePath in availableFiles)
            {
                var normalizedPath = NormalizePath(filePath);
                if (ShouldReadFile(normalizedPath, ignoreCheckpoints))
                {
                    filesToRead.Add(normalizedPath);
                }
            }

            return filesToRead;
        }

        public void OnFileChanged(string filePath)
        {
            var normalizedPath = NormalizePath(filePath);
            var fileId = GetFileId(normalizedPath);
            if (_checkpoints.TryGetValue(fileId, out var checkpoint))
            {
                var fileInfo = new FileInfo(normalizedPath);
                if (fileInfo.Exists && fileInfo.LastWriteTimeUtc > checkpoint.LastModified)
                {
                    // File has been modified since last checkpoint, update metadata
                    var updatedCheckpoint = checkpoint with
                    {
                        FileSize = fileInfo.Length,
                        LastModified = fileInfo.LastWriteTimeUtc
                    };
                    _checkpoints.TryUpdate(fileId, updatedCheckpoint, checkpoint);
                }
            }
        }

        private bool ShouldReadFile(string filePath, bool ignoreCheckpoints)
        {
            if (ignoreCheckpoints)
            {
                return true;
            }

            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
            {
                return false;
            }

            var fileId = GetFileId(filePath);
            if (!_checkpoints.TryGetValue(fileId, out var checkpoint))
            {
                // No checkpoint exists, should read the file
                return true;
            }

            // Check if file has been modified or grown since last checkpoint
            if (fileInfo.LastWriteTimeUtc > checkpoint.LastModified ||
                fileInfo.Length > checkpoint.FileSize ||
                checkpoint.Position < fileInfo.Length)
            {
                return true;
            }

            return false;
        }

        public bool ShouldResumeFromCheckpoint(string filePath, bool ignoreCheckpoints)
        {
            if (ignoreCheckpoints)
            {
                return false;
            }

            var normalizedPath = NormalizePath(filePath);
            var fileId = GetFileId(normalizedPath);
            if (!_checkpoints.TryGetValue(fileId, out var checkpoint))
            {
                return false;
            }

            var fileInfo = new FileInfo(normalizedPath);
            if (!fileInfo.Exists)
            {
                return false;
            }

            return checkpoint.Position <= fileInfo.Length
                && checkpoint.LastModified <= fileInfo.LastWriteTimeUtc;
        }

        private void LoadCheckpoints()
        {
            try
            {
                var checkpointFiles = Directory.GetFiles(_dataDirectory, "*.checkpoint");
                foreach (var file in checkpointFiles)
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var checkpoint = JsonSerializer.Deserialize(json, LogFileSourceGenerationContext.Default.CheckpointInfo);
                        if (checkpoint != null)
                        {
                            var fileId = Path.GetFileNameWithoutExtension(file);
                            _checkpoints.TryAdd(fileId, checkpoint);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogFileServiceLog.CheckpointError(this._logger, file, ex);
                        File.Delete(file);
                    }
                }
            }
            catch (Exception ex)
            {
                LogFileServiceLog.LoadCheckPointError(this._logger, ex);
            }
        }

        private void PersistSingleCheckpoint(string fileId, CheckpointInfo checkpoint)
        {
            if (_disposed)
            {
                return;
            }

            lock (_mutex)
            {
                try
                {
                    var filePath = Path.Combine(_dataDirectory, $"{fileId}.checkpoint");
                    var json = JsonSerializer.Serialize(checkpoint, LogFileSourceGenerationContext.Default.CheckpointInfo);
                    File.WriteAllText(filePath, json);
                }
                catch (Exception ex)
                {
                    LogFileServiceLog.WriteCheckPointError(_logger, fileId, ex);
                }
            }
        }

        private void PersistAllCheckpoints()
        {
            if (_disposed)
            {
                return;
            }

            lock (_mutex)
            {
                foreach (var kvp in _checkpoints)
                {
                    try
                    {
                        var filePath = Path.Combine(_dataDirectory, $"{kvp.Key}.checkpoint");
                        var json = JsonSerializer.Serialize(kvp.Value, LogFileSourceGenerationContext.Default.CheckpointInfo);
                        File.WriteAllText(filePath, json);
                    }
                    catch (Exception ex)
                    {
                        LogFileServiceLog.WriteCheckPointError(_logger, kvp.Key, ex);
                    }
                }
            }
        }

        private static string NormalizePath(string filePath)
        {
            // Normalize the path to handle spaces and special characters consistently
            return Path.GetFullPath(filePath);
        }

        private static string GetFileId(string filePath)
        {
            // Preserve case sensitivity based on the operating system
            // Windows file systems are case-insensitive, Unix/Linux are case-sensitive
            var path = OperatingSystem.IsWindows()
                ? filePath.ToLowerInvariant()
                : filePath;

            var bytes = Encoding.UTF8.GetBytes(path);
#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
            var hash = SHA1.HashData(bytes);
#pragma warning restore CA5350 // Do Not Use Weak Cryptographic Algorithms
            return Convert.ToHexString(hash);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                PersistAllCheckpoints();
            }
        }

    }
}
