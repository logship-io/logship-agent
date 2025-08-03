// <copyright file="FileGlobMatcher.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Logship.Agent.Core.Services.Sources.Common.LogFile
{
    internal sealed class FileGlobMatcher
    {
        private readonly Matcher _matcher;
        private readonly string[] _includePatterns;
        private readonly string[] _excludePatterns;
        private readonly string _workingDirectory;

        public FileGlobMatcher(string[] includePatterns, string[] excludePatterns, string? workingDirectory = null)
        {
            _includePatterns = includePatterns ?? Array.Empty<string>();
            _excludePatterns = excludePatterns ?? Array.Empty<string>();
            _workingDirectory = Path.GetFullPath(workingDirectory ?? Directory.GetCurrentDirectory());
            _matcher = new Matcher();

            foreach (var pattern in _includePatterns)
            {
                _matcher.AddInclude(pattern);
            }

            foreach (var pattern in _excludePatterns)
            {
                _matcher.AddExclude(pattern);
            }
        }

        public IEnumerable<string> GetMatchingFiles()
        {
            if (!Directory.Exists(_workingDirectory))
            {
                return [];
            }

            var directoryInfo = new DirectoryInfo(_workingDirectory);
            var directoryInfoWrapper = new DirectoryInfoWrapper(directoryInfo);

            var result = _matcher.Execute(directoryInfoWrapper);
            return result.Files.Select(file => Path.GetFullPath(Path.Combine(_workingDirectory, file.Path)));
        }
    }
}
