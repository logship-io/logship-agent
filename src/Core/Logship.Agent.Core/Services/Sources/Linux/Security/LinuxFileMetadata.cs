// <copyright file="LinuxFileMetadata.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using System.Globalization;

namespace Logship.Agent.Core.Services.Sources.Linux.Security
{
    internal static class LinuxFileMetadata
    {
        public static void AddFileInfo(Dictionary<string, object> data, FileSystemInfo info)
        {
            data["Path"] = info.FullName;
            data["Name"] = info.Name;
            data["Exists"] = info.Exists;
            if (false == info.Exists)
            {
                return;
            }

            data["LastWriteTimeUtc"] = info.LastWriteTimeUtc;
            data["CreationTimeUtc"] = info.CreationTimeUtc;
            data["Attributes"] = info.Attributes.ToString();

            if (info is FileInfo file)
            {
                data["LengthBytes"] = file.Length;
            }

            try
            {
                var mode = info.UnixFileMode;
                data["UnixFileMode"] = mode.ToString();
                data["UnixFileModeOctal"] = Convert.ToString((int)mode, 8).PadLeft(4, '0');
            }
            catch (Exception) when (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
            {
                data["UnixFileMode"] = string.Empty;
                data["UnixFileModeOctal"] = string.Empty;
            }
        }

        public static string NormalizePath(string basePath, string includePath)
        {
            if (Path.IsPathRooted(includePath))
            {
                return includePath;
            }

            var directory = Path.GetDirectoryName(basePath);
            if (string.IsNullOrEmpty(directory))
            {
                return includePath;
            }

            return Path.GetFullPath(Path.Combine(directory, includePath));
        }

        public static IEnumerable<string> ExpandGlob(string pattern)
        {
            var directory = Path.GetDirectoryName(pattern);
            var search = Path.GetFileName(pattern);
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(search))
            {
                yield break;
            }

            if (false == Directory.Exists(directory))
            {
                yield break;
            }

            foreach (var file in Directory.EnumerateFiles(directory, search, SearchOption.TopDirectoryOnly))
            {
                yield return file;
            }
        }

        public static string ToInvariantString(object? value)
        {
            return value switch
            {
                null => string.Empty,
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty,
            };
        }
    }
}
