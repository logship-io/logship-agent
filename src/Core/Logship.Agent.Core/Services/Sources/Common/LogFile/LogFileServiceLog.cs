using Microsoft.Extensions.Logging;

namespace Logship.Agent.Core.Services.Sources.Common.LogFile
{
    internal static partial class LogFileServiceLog
    {
        [LoggerMessage(LogLevel.Information, "Starting log file monitoring for {PatternCount} patterns")]
        public static partial void StartingMonitoring(ILogger logger, int patternCount);

        [LoggerMessage(LogLevel.Debug, "Found {FileCount} files matching patterns")]
        public static partial void FilesFound(ILogger logger, int fileCount);

        [LoggerMessage(LogLevel.Warning, "File access denied: {FilePath}")]
        public static partial void FileAccessDenied(ILogger logger, string filePath);

        [LoggerMessage(LogLevel.Error, "Error reading file {FilePath} at position {Position}")]
        public static partial void FileReadError(ILogger logger, string filePath, long position, Exception ex);

        [LoggerMessage(LogLevel.Debug, "Checkpoint saved for {FilePath} at position {Position}")]
        public static partial void CheckpointSaved(ILogger logger, string filePath, long position);

        [LoggerMessage(LogLevel.Information, "Multiline log completed with {LineCount} lines, ID: {MultilineId}")]
        public static partial void MultilineCompleted(ILogger logger, int lineCount, string multilineId);

        [LoggerMessage(LogLevel.Warning, "Multiline timeout reached for file {FilePath}, forcing completion")]
        public static partial void MultilineTimeout(ILogger logger, string filePath);

        [LoggerMessage(LogLevel.Information, "File rotation detected for {OldPath}, new file: {NewPath}")]
        public static partial void FileRotation(ILogger logger, string oldPath, string newPath);

        [LoggerMessage(LogLevel.Debug, "Processing {ByteCount} bytes from {FilePath}")]
        public static partial void ProcessingBytes(ILogger logger, int byteCount, string filePath);

        [LoggerMessage(LogLevel.Debug, "Starting file watch for pattern: {Pattern}")]
        public static partial void StartingFileWatch(ILogger logger, string pattern);

        [LoggerMessage(LogLevel.Warning, "File not found: {FilePath}")]
        public static partial void FileNotFound(ILogger logger, string filePath);

        [LoggerMessage(LogLevel.Trace, "File {FilePath} ignored due to age: {FileAge} seconds")]
        public static partial void FileIgnoredAge(ILogger logger, string filePath, double fileAge);

        [LoggerMessage(LogLevel.Debug, "Line too long in file {FilePath}: {LineLength} bytes, truncating")]
        public static partial void LineTooLong(ILogger logger, string filePath, int lineLength);

        [LoggerMessage(LogLevel.Information, "Resuming file {FilePath} from checkpoint position {Position}")]
        public static partial void ResumingFromCheckpoint(ILogger logger, string filePath, long position);

        [LoggerMessage(LogLevel.Debug, "File {FilePath} size changed from {OldSize} to {NewSize}")]
        public static partial void FileSizeChanged(ILogger logger, string filePath, long oldSize, long newSize);

        [LoggerMessage(LogLevel.Warning, "No include patterns specified for LogFileService")]
        public static partial void NoIncludePatterns(ILogger logger);

        [LoggerMessage(LogLevel.Error, "Error during file processing")]
        public static partial void FileProcessingError(ILogger logger, Exception ex);

        [LoggerMessage(LogLevel.Error, "Error during checkpoint read at {Path}")]
        public static partial void CheckpointError(ILogger logger, string path, Exception ex);

        [LoggerMessage(LogLevel.Error, "Error during while loading checkpoints")]
        public static partial void LoadCheckPointError(ILogger logger, Exception ex);

        [LoggerMessage(LogLevel.Error, "Failed to write checkpoint {Checkpoint}")]
        public static partial void WriteCheckPointError(ILogger logger, string checkpoint, Exception ex);
    }
}