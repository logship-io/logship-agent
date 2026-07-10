// <copyright file="LinuxSshConfigParser.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

namespace Logship.Agent.Core.Services.Sources.Linux.Security
{
    internal sealed class LinuxSshConfigParseResult
    {
        public Dictionary<string, string> Settings { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> FilesRead { get; } = new();

        public List<string> IncludePatterns { get; } = new();
    }

    internal static class LinuxSshConfigParser
    {
        private const int MaxDepth = 8;

        public static LinuxSshConfigParseResult Parse(string configPath)
        {
            var result = new LinuxSshConfigParseResult();
            ParseFile(configPath, result, new HashSet<string>(StringComparer.Ordinal), 0);
            return result;
        }

        private static void ParseFile(string configPath, LinuxSshConfigParseResult result, HashSet<string> visited, int depth)
        {
            if (depth > MaxDepth || false == File.Exists(configPath))
            {
                return;
            }

            var fullPath = Path.GetFullPath(configPath);
            if (false == visited.Add(fullPath))
            {
                return;
            }

            result.FilesRead.Add(fullPath);

            foreach (var rawLine in File.ReadLines(fullPath))
            {
                var line = StripComment(rawLine).Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                var split = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
                if (split.Length == 0)
                {
                    continue;
                }

                var key = split[0];
                var value = split.Length > 1 ? split[1].Trim() : string.Empty;
                if (key.Equals("Include", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var include in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var includePattern = LinuxFileMetadata.NormalizePath(fullPath, include);
                        result.IncludePatterns.Add(includePattern);
                        foreach (var file in LinuxFileMetadata.ExpandGlob(includePattern))
                        {
                            ParseFile(file, result, visited, depth + 1);
                        }
                    }

                    continue;
                }

                result.Settings[key] = value;
            }
        }

        private static string StripComment(string line)
        {
            var escaped = false;
            for (var i = 0; i < line.Length; i++)
            {
                if (line[i] == '\\')
                {
                    escaped = !escaped;
                    continue;
                }

                if (line[i] == '#' && false == escaped)
                {
                    return line.Substring(0, i);
                }

                escaped = false;
            }

            return line;
        }
    }
}
