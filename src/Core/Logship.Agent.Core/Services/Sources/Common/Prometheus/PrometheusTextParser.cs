// <copyright file="PrometheusTextParser.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Logship.Agent.Core.Services.Sources.Common.Prometheus
{
    internal sealed record PrometheusSample(string Name, IReadOnlyDictionary<string, string> Labels, double Value, DateTimeOffset? Timestamp);

    internal sealed record PrometheusParseResult(IReadOnlyList<PrometheusSample> Samples, int MalformedLines);

    /// <summary>
    /// Parses the Prometheus text exposition format, including the UTF-8 quoted-name
    /// syntax introduced in Prometheus 3.0 ({"metric.name", "label.name"="value"} 1).
    /// OpenMetrics exemplars (a trailing "# {...}") and the "# EOF" marker are ignored.
    /// </summary>
    internal static class PrometheusTextParser
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyLabels = new Dictionary<string, string>();

        // Matches the response buffer cap previously enforced via HttpClient.MaxResponseContentBufferSize.
        private const long MaxInputChars = 32L * 1024 * 1024;

        // Range accepted by DateTimeOffset.FromUnixTimeMilliseconds.
        private const long MinUnixMilliseconds = -62_135_596_800_000;
        private const long MaxUnixMilliseconds = 253_402_300_799_999;

        public static async Task<PrometheusParseResult> ParseAsync(Stream stream, CancellationToken cancellationToken)
        {
            var samples = new List<PrometheusSample>();
            int malformedLines = 0;
            long totalChars = 0;

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            while (await reader.ReadLineAsync(cancellationToken) is { } rawLine)
            {
                totalChars += rawLine.Length;
                if (totalChars > MaxInputChars)
                {
                    throw new InvalidDataException($"Prometheus text input exceeded the maximum size of {MaxInputChars} characters.");
                }

                var line = rawLine.AsSpan().Trim(" \t");
                if (line.IsEmpty || line[0] == '#')
                {
                    continue;
                }

                if (TryParseSampleLine(line, out var sample))
                {
                    samples.Add(sample);
                }
                else
                {
                    malformedLines++;
                }
            }

            return new PrometheusParseResult(samples, malformedLines);
        }

        private static bool TryParseSampleLine(ReadOnlySpan<char> line, [NotNullWhen(true)] out PrometheusSample? sample)
        {
            sample = null;

            string? name = null;
            Dictionary<string, string>? labels = null;
            int i = 0;

            if (line[0] != '{')
            {
                while (i < line.Length && line[i] != '{' && line[i] != ' ' && line[i] != '\t')
                {
                    i++;
                }

                name = line[..i].ToString();
                SkipBlanks(line, ref i);
            }

            if (i < line.Length && line[i] == '{')
            {
                i++;
                labels = new Dictionary<string, string>(StringComparer.Ordinal);
                if (false == TryParseBraceBody(line, ref i, labels, ref name))
                {
                    return false;
                }
            }

            if (name is null)
            {
                return false;
            }

            SkipBlanks(line, ref i);
            int valueStart = i;
            while (i < line.Length && line[i] != ' ' && line[i] != '\t')
            {
                i++;
            }

            if (i == valueStart)
            {
                return false;
            }

            if (false == TryParseValue(line[valueStart..i], out var value))
            {
                return false;
            }

            SkipBlanks(line, ref i);
            DateTimeOffset? timestamp = null;
            if (i < line.Length && line[i] != '#')
            {
                int timestampStart = i;
                while (i < line.Length && line[i] != ' ' && line[i] != '\t')
                {
                    i++;
                }

                if (false == long.TryParse(line[timestampStart..i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var unixMilliseconds)
                    || unixMilliseconds < MinUnixMilliseconds
                    || unixMilliseconds > MaxUnixMilliseconds)
                {
                    return false;
                }

                timestamp = DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds);
                SkipBlanks(line, ref i);
            }

            // Anything left must be an OpenMetrics exemplar ("# {...} value"), which is ignored.
            if (i < line.Length && line[i] != '#')
            {
                return false;
            }

            sample = new PrometheusSample(name, labels ?? EmptyLabels, value, timestamp);
            return true;
        }

        private static bool TryParseBraceBody(ReadOnlySpan<char> line, ref int i, Dictionary<string, string> labels, ref string? metricName)
        {
            for (int item = 0; ; item++)
            {
                SkipBlanks(line, ref i);
                if (i >= line.Length)
                {
                    return false;
                }

                if (line[i] == '}')
                {
                    i++;
                    return true;
                }

                string key;
                bool quotedKey = false;
                if (line[i] == '"')
                {
                    i++;
                    if (false == TryParseQuotedString(line, ref i, out var quoted))
                    {
                        return false;
                    }

                    key = quoted;
                    quotedKey = true;
                }
                else
                {
                    int keyStart = i;
                    while (i < line.Length && line[i] != '=' && line[i] != ',' && line[i] != '}' && line[i] != ' ' && line[i] != '\t')
                    {
                        i++;
                    }

                    if (i == keyStart)
                    {
                        return false;
                    }

                    key = line[keyStart..i].ToString();
                }

                SkipBlanks(line, ref i);
                if (i >= line.Length)
                {
                    return false;
                }

                if (line[i] == '=')
                {
                    i++;
                    SkipBlanks(line, ref i);
                    if (i >= line.Length || line[i] != '"')
                    {
                        return false;
                    }

                    i++;
                    if (false == TryParseQuotedString(line, ref i, out var labelValue))
                    {
                        return false;
                    }

                    labels[key] = labelValue;
                }
                else if (quotedKey && item == 0 && metricName is null)
                {
                    // UTF-8 syntax: a quoted first item without "=" is the metric name.
                    metricName = key;
                }
                else
                {
                    return false;
                }

                SkipBlanks(line, ref i);
                if (i >= line.Length)
                {
                    return false;
                }

                if (line[i] == ',')
                {
                    i++;
                    continue;
                }

                if (line[i] == '}')
                {
                    i++;
                    return true;
                }

                return false;
            }
        }

        private static bool TryParseQuotedString(ReadOnlySpan<char> line, ref int i, [NotNullWhen(true)] out string? value)
        {
            int start = i;
            while (i < line.Length)
            {
                char c = line[i];
                if (c == '"')
                {
                    value = line[start..i].ToString();
                    i++;
                    return true;
                }

                if (c == '\\')
                {
                    return TryParseEscapedString(line, ref i, start, out value);
                }

                i++;
            }

            value = null;
            return false;
        }

        private static bool TryParseEscapedString(ReadOnlySpan<char> line, ref int i, int start, [NotNullWhen(true)] out string? value)
        {
            value = null;
            var builder = new StringBuilder(line.Length - start);
            builder.Append(line[start..i]);
            while (i < line.Length)
            {
                char c = line[i];
                if (c == '"')
                {
                    i++;
                    value = builder.ToString();
                    return true;
                }

                if (c == '\\')
                {
                    if (i + 1 >= line.Length)
                    {
                        return false;
                    }

                    char escaped = line[i + 1];
                    switch (escaped)
                    {
                        case '\\':
                            builder.Append('\\');
                            break;
                        case '"':
                            builder.Append('"');
                            break;
                        case 'n':
                            builder.Append('\n');
                            break;
                        default:
                            // Unknown escape sequences are kept literally.
                            builder.Append(c).Append(escaped);
                            break;
                    }

                    i += 2;
                    continue;
                }

                builder.Append(c);
                i++;
            }

            return false;
        }

        private static bool TryParseValue(ReadOnlySpan<char> token, out double value)
        {
            if (token.Equals("Inf", StringComparison.OrdinalIgnoreCase) || token.Equals("+Inf", StringComparison.OrdinalIgnoreCase))
            {
                value = double.PositiveInfinity;
                return true;
            }

            if (token.Equals("-Inf", StringComparison.OrdinalIgnoreCase))
            {
                value = double.NegativeInfinity;
                return true;
            }

            return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static void SkipBlanks(ReadOnlySpan<char> line, ref int i)
        {
            while (i < line.Length && char.IsWhiteSpace(line[i]))
            {
                i++;
            }
        }
    }
}
