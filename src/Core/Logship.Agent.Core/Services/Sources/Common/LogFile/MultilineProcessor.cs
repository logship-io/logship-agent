using System.Text;
using System.Text.RegularExpressions;

namespace Logship.Agent.Core.Services.Sources.Common.LogFile
{
    internal sealed class MultilineProcessor
    {
        private readonly MultilineConfiguration? _config;
        private readonly Regex? _startPatternRegex;
        private readonly Regex? _conditionPatternRegex;
        private readonly StringBuilder _currentMultilineBuffer;
        private DateTime _lastLineTime;
        private bool _hasBufferedContent;
        private string? _currentMultilineId;

        public MultilineProcessor(MultilineConfiguration? config)
        {
            _config = config;
            _currentMultilineBuffer = new StringBuilder();
            _lastLineTime = DateTime.UtcNow;

            if (_config?.StartPattern != null)
            {
                _startPatternRegex = new Regex(_config.StartPattern, RegexOptions.Compiled | RegexOptions.Multiline);
            }

            if (_config?.ConditionPattern != null)
            {
                _conditionPatternRegex = new Regex(_config.ConditionPattern, RegexOptions.Compiled | RegexOptions.Multiline);
            }
        }

        public IEnumerable<ProcessedLine> ProcessLine(string line, long lineNumber, long byteOffset)
        {
            var results = new List<ProcessedLine>();

            if (_config == null)
            {
                // No multiline processing, return single line
                results.Add(new ProcessedLine(line, lineNumber, byteOffset, null));
                return results;
            }

            var currentTime = DateTime.UtcNow;
            var isTimeout = _hasBufferedContent &&
                           (currentTime - _lastLineTime).TotalMilliseconds > _config.TimeoutMs;

            var isStartLine = _startPatternRegex?.IsMatch(line) ?? false;

            // If we have buffered content and either timeout or new start line, emit the buffer
            if (_hasBufferedContent && (isTimeout || isStartLine))
            {
                if (_currentMultilineBuffer.Length > 0)
                {
                    var bufferedContent = _currentMultilineBuffer.ToString().TrimEnd('\r', '\n');
                    results.Add(new ProcessedLine(bufferedContent, lineNumber - 1, byteOffset, _currentMultilineId));
                    _currentMultilineBuffer.Clear();
                }
                _hasBufferedContent = false;
                _currentMultilineId = null;
            }

            // Start new multiline buffer if this is a start line
            if (isStartLine)
            {
                _currentMultilineId = Guid.NewGuid().ToString();
                _currentMultilineBuffer.Clear();
                _hasBufferedContent = true;

                // Add the start line to buffer
                _currentMultilineBuffer.Append(line);
                _lastLineTime = currentTime;
            }
            else if (_hasBufferedContent)
            {
                // Continue building multiline - add line to buffer
                _currentMultilineBuffer.AppendLine();
                _currentMultilineBuffer.Append(line);
                _lastLineTime = currentTime;
            }
            else
            {
                // Single line that doesn't match start pattern and no buffer
                results.Add(new ProcessedLine(line, lineNumber, byteOffset, null));
            }

            return results;
        }

        public ProcessedLine? FlushBuffer()
        {
            if (!_hasBufferedContent || _currentMultilineBuffer.Length == 0)
            {
                return null;
            }

            var content = _currentMultilineBuffer.ToString().TrimEnd('\r', '\n');
            var result = new ProcessedLine(content, -1, -1, _currentMultilineId);

            _currentMultilineBuffer.Clear();
            _hasBufferedContent = false;

            return result;
        }
    }

    internal sealed record ProcessedLine(string Content, long LineNumber, long ByteOffset, string? MultilineId);
}