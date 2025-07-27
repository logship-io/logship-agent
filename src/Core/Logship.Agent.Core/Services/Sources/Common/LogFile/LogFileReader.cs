using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Text;

namespace Logship.Agent.Core.Services.Sources.Common.LogFile
{
    internal sealed class LogFileReader : IDisposable
    {
        private readonly string _filePath;
        private readonly LogFileServiceConfiguration _config;
        private readonly ILogger _logger;
        private readonly Encoding _encoding;
        private readonly MultilineProcessor _multilineProcessor;
        private FileStream? _fileStream;
        private long _currentPosition;
        private readonly byte[] _buffer;
        private readonly StringBuilder _lineBuffer;
        private bool _disposed;

        public string FilePath => _filePath;
        public long CurrentPosition => _currentPosition;

        public LogFileReader(string filePath, LogFileServiceConfiguration config, ILogger logger)
        {
            _filePath = filePath;
            _config = config;
            _logger = logger;
            _encoding = GetEncoding(config.Encoding);
            _multilineProcessor = new MultilineProcessor(config.Multiline);
            _buffer = new byte[config.ReadBufferSize];
            _lineBuffer = new StringBuilder();
        }

        public void SetPosition(long position)
        {
            _currentPosition = position;
            _fileStream?.Seek(position, SeekOrigin.Begin);
        }

        public async IAsyncEnumerable<ProcessedLine> ReadLinesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            EnsureFileStreamOpen();

            if (_fileStream == null)
            {
                yield break;
            }

            var remainingBytes = Array.Empty<byte>();
            long lineNumber = 1;

            while (!cancellationToken.IsCancellationRequested)
            {
                int bytesRead;
                try
                {
                    bytesRead = await _fileStream.ReadAsync(_buffer.AsMemory(), cancellationToken);
                }
                catch (Exception ex)
                {
                    LogFileServiceLog.FileReadError(_logger, _filePath, _currentPosition, ex);
                    yield break;
                }

                if (bytesRead == 0)
                {
                    // No more data available
                    if (remainingBytes.Length > 0)
                    {
                        // Process any remaining bytes as the final line
                        var finalLine = _encoding.GetString(remainingBytes);
                        foreach (var processedLine in _multilineProcessor.ProcessLine(finalLine, lineNumber, _currentPosition))
                        {
                            yield return processedLine;
                        }
                    }
                    break;
                }

                LogFileServiceLog.ProcessingBytes(_logger, bytesRead, _filePath);

                var allBytes = remainingBytes.Length > 0
                    ? remainingBytes.Concat(_buffer.AsSpan(0, bytesRead).ToArray()).ToArray()
                    : _buffer.AsSpan(0, bytesRead).ToArray();

                var (lines, remaining) = ExtractLines(allBytes);
                remainingBytes = remaining;

                foreach (var line in lines)
                {
                    if (line.Length > _config.MaxLineBytes)
                    {
                        LogFileServiceLog.LineTooLong(_logger, _filePath, line.Length);
                        var truncatedLine = line.Substring(0, _config.MaxLineBytes);
                        foreach (var processedLine in _multilineProcessor.ProcessLine(truncatedLine, lineNumber, _currentPosition))
                        {
                            yield return processedLine;
                        }
                    }
                    else
                    {
                        foreach (var processedLine in _multilineProcessor.ProcessLine(line, lineNumber, _currentPosition))
                        {
                            yield return processedLine;
                        }
                    }
                    lineNumber++;
                }

                _currentPosition = _fileStream.Position - remainingBytes.Length;
            }

            // Flush any remaining multiline content
            var flushedLine = _multilineProcessor.FlushBuffer();
            if (flushedLine != null)
            {
                yield return flushedLine;
            }
        }

        private void EnsureFileStreamOpen()
        {
            if (_fileStream?.CanRead == true)
            {
                return;
            }

            try
            {
                _fileStream?.Dispose();
                _fileStream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                _fileStream.Seek(_currentPosition, SeekOrigin.Begin);
            }
            catch (Exception ex)
            {
                LogFileServiceLog.FileReadError(_logger, _filePath, _currentPosition, ex);
                throw;
            }
        }

        private (List<string> lines, byte[] remaining) ExtractLines(byte[] bytes)
        {
            var lines = new List<string>();
            var remaining = Array.Empty<byte>();
            var position = 0;

            while (position < bytes.Length)
            {
                var newlineIndex = FindNewline(bytes, position);

                if (newlineIndex == -1)
                {
                    // No newline found, save remaining bytes
                    remaining = bytes[position..];
                    break;
                }

                var lineBytes = bytes[position..newlineIndex];
                var line = _encoding.GetString(lineBytes);
                lines.Add(line);

                // Skip the newline character(s)
                position = newlineIndex + 1;
                if (position < bytes.Length && bytes[newlineIndex] == '\r' && bytes[position] == '\n')
                {
                    position++; // Skip \r\n
                }
            }

            return (lines, remaining);
        }

        private static int FindNewline(byte[] bytes, int startIndex)
        {
            for (int i = startIndex; i < bytes.Length; i++)
            {
                if (bytes[i] == '\n' || bytes[i] == '\r')
                {
                    return i;
                }
            }
            return -1;
        }

        private static Encoding GetEncoding(string encodingName)
        {
            try
            {
                return Encoding.GetEncoding(encodingName);
            }
            catch
            {
                return Encoding.UTF8;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _fileStream?.Dispose();
            }
        }
    }
}