// <copyright file="JsonLinesProcessor.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Globalization;

namespace Logship.Agent.Core.Services.Sources.Common.LogFile
{
    internal sealed class JsonLinesProcessor
    {
        private readonly JsonLinesConfiguration? _config;
        private readonly ILogger _logger;

        public JsonLinesProcessor(JsonLinesConfiguration? config, ILogger logger)
        {
            _config = config;
            _logger = logger;
        }

        public bool IsEnabled => _config?.Enabled == true;

        public (Dictionary<string, object> data, bool success) ProcessJsonLine(string line, long lineNumber, string filePath)
        {
            if (!IsEnabled)
            {
                return (new Dictionary<string, object>(), false);
            }

            try
            {
                using var jsonDocument = JsonDocument.Parse(line);
                var data = new Dictionary<string, object>();

                // Always flatten using '_' separator
                Flatten(jsonDocument.RootElement, data, string.Empty);

                // Extract timestamp if specified
                if (!string.IsNullOrEmpty(_config!.TimestampField) && data.TryGetValue(_config.TimestampField, out var timestampValue))
                {
                    if (TryParseTimestamp(timestampValue, out var parsedTimestamp))
                    {
                        data["ParsedTimestamp"] = parsedTimestamp;
                    }
                }

                // Extract message field if specified
                if (!string.IsNullOrEmpty(_config.MessageField) && data.TryGetValue(_config.MessageField, out var messageValue))
                {
                    data["Message"] = messageValue;
                }

                return (data, true);
            }
            catch (JsonException ex)
            {
                LogFileServiceLog.JsonParseError(_logger, filePath, lineNumber, ex);

                if (_config!.SkipInvalidLines)
                {
                    return (new Dictionary<string, object>(), false);
                }

                return (new Dictionary<string, object> { { "RawContent", line }, { "ParseError", ex.Message } }, true);
            }
        }

        private static void Flatten(JsonElement element, Dictionary<string, object> data, string prefix)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        var nextPrefix = string.IsNullOrEmpty(prefix) ? property.Name : prefix + '_' + property.Name;
                        Flatten(property.Value, data, nextPrefix);
                    }
                    break;
                case JsonValueKind.Array:
                    var index = 0;
                    foreach (var item in element.EnumerateArray())
                    {
                        var nextPrefix = string.IsNullOrEmpty(prefix) ? index.ToString(CultureInfo.InvariantCulture) : prefix + '_' + index.ToString(CultureInfo.InvariantCulture);
                        Flatten(item, data, nextPrefix);
                        index++;
                    }
                    // Empty array case: record as empty string
                    if (index == 0 && prefix.Length > 0)
                    {
                        data[prefix] = string.Empty;
                    }
                    break;
                default:
                    var key = prefix.Length == 0 ? "Value" : prefix; // Root primitive becomes "Value"
                    data[key] = GetSimpleValue(element);
                    break;
            }
        }

        private static object GetSimpleValue(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.TryGetInt64(out var longValue) ? (object)longValue : (element.TryGetDouble(out var doubleValue) ? doubleValue : 0d),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => string.Empty,
            // For nested object/array terminal primitives we already recurse; unreachable cases default to raw text
            _ => element.GetRawText()
        };

        private static bool TryParseTimestamp(object? timestampValue, out DateTimeOffset timestamp)
        {
            timestamp = default;
            switch (timestampValue)
            {
                case null:
                    return false;
                case string s:
                    if (DateTimeOffset.TryParse(s, out timestamp)) return true;
                    if (long.TryParse(s, out var parsedLong))
                    {
                        try
                        {
                            timestamp = parsedLong > 1_000_000_000_000L ? DateTimeOffset.FromUnixTimeMilliseconds(parsedLong) : DateTimeOffset.FromUnixTimeSeconds(parsedLong);
                            return true;
                        }
                        catch { }
                    }
                    break;
                case long l:
                    try
                    {
                        timestamp = l > 1_000_000_000_000L ? DateTimeOffset.FromUnixTimeMilliseconds(l) : DateTimeOffset.FromUnixTimeSeconds(l);
                        return true;
                    }
                    catch { }
                    break;
                case double d:
                    try
                    {
                        var seconds = (long)d;
                        var ms = (int)((d - seconds) * 1000);
                        timestamp = DateTimeOffset.FromUnixTimeSeconds(seconds).AddMilliseconds(ms);
                        return true;
                    }
                    catch { }
                    break;
            }
            return false;
        }
    }
}