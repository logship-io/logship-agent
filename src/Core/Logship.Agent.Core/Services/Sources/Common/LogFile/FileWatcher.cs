using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Logship.Agent.Core.Services.Sources.Common.LogFile
{
    internal sealed class FileWatcher : IDisposable
    {
        private readonly LogFileServiceConfiguration _config;
        private readonly ILogger _logger;
        private readonly FileGlobMatcher _globMatcher;
        private readonly ConcurrentDictionary<string, DateTime> _lastGlobScan;
        private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers;
        private bool _disposed;

        public FileWatcher(LogFileServiceConfiguration config, ILogger logger)
        {
            _config = config;
            _logger = logger;
            _globMatcher = new FileGlobMatcher(config.Include, config.Exclude, config.WorkingDirectory);
            _lastGlobScan = new ConcurrentDictionary<string, DateTime>();
            _watchers = new ConcurrentDictionary<string, FileSystemWatcher>();
        }

        public IEnumerable<string> GetCurrentFiles()
        {
            var currentTime = DateTime.UtcNow;
            var files = new List<string>();

            try
            {
                var matchingFiles = _globMatcher.GetMatchingFiles();

                foreach (var filePath in matchingFiles)
                {
                    if (ShouldProcessFile(filePath, currentTime))
                    {
                        files.Add(filePath);
                    }
                }

                LogFileServiceLog.FilesFound(_logger, files.Count);
            }
            catch (Exception ex)
            {
                LogFileServiceLog.FileReadError(_logger, "glob scan", 0, ex);
            }

            return files;
        }

        public bool ShouldRescanFiles()
        {
            var now = DateTime.UtcNow;

            // Always rescan on first call (empty collection)
            if (_lastGlobScan.IsEmpty && _config.Include.Length > 0)
            {
                foreach (var pattern in _config.Include)
                {
                    _lastGlobScan.TryAdd(pattern, now);
                }
                return true;
            }

            var shouldRescan = _lastGlobScan.Values.All(lastScan =>
                (now - lastScan).TotalMilliseconds >= _config.GlobMinimumCooldownMs);

            if (shouldRescan)
            {
                foreach (var pattern in _config.Include)
                {
                    _lastGlobScan.AddOrUpdate(pattern, now, (_, _) => now);
                }
            }

            return shouldRescan;
        }

        public void StartWatching(IEnumerable<string> filePaths, Action<string, FileSystemEventArgs> onFileChanged)
        {
            var directoriesToWatch = filePaths
                .Select(Path.GetDirectoryName)
                .Where(dir => !string.IsNullOrEmpty(dir))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var directory in directoriesToWatch)
            {
                if (!_watchers.ContainsKey(directory) && Directory.Exists(directory))
                {
                    try
                    {
                        var watcher = new FileSystemWatcher(directory)
                        {
                            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                            IncludeSubdirectories = false,
                            EnableRaisingEvents = true
                        };

                        watcher.Changed += (sender, e) => onFileChanged(directory, e);
                        watcher.Created += (sender, e) => onFileChanged(directory, e);

                        _watchers.TryAdd(directory, watcher);
                        LogFileServiceLog.StartingFileWatch(_logger, directory);
                    }
                    catch (Exception ex)
                    {
                        LogFileServiceLog.FileReadError(_logger, directory, 0, ex);
                    }
                }
            }
        }

        private bool ShouldProcessFile(string filePath, DateTime currentTime)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);

                if (!fileInfo.Exists)
                {
                    LogFileServiceLog.FileNotFound(_logger, filePath);
                    return false;
                }

                if (_config.IgnoreOlderSecs.HasValue)
                {
                    var fileAge = (currentTime - fileInfo.LastWriteTimeUtc).TotalSeconds;
                    if (fileAge > _config.IgnoreOlderSecs.Value)
                    {
                        LogFileServiceLog.FileIgnoredAge(_logger, filePath, fileAge);
                        return false;
                    }
                }

                return true;
            }
            catch (UnauthorizedAccessException)
            {
                LogFileServiceLog.FileAccessDenied(_logger, filePath);
                return false;
            }
            catch (Exception ex)
            {
                LogFileServiceLog.FileReadError(_logger, filePath, 0, ex);
                return false;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                foreach (var watcher in _watchers.Values)
                {
                    try
                    {
                        watcher?.Dispose();
                    }
                    catch
                    {
                        // Ignore disposal errors
                    }
                }

                _watchers.Clear();
            }
        }
    }
}