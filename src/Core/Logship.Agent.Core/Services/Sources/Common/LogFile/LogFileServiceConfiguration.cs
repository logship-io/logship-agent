// <copyright file="LogFileServiceConfiguration.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Microsoft.Extensions.Configuration;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Logship.Agent.Core.Services.Sources.Common.LogFile
{
    public sealed class LogFileServiceConfiguration : BaseInputConfiguration
    {
        [JsonPropertyName("include")]
        [ConfigurationKeyName("include")]
        public string[] Include { get; set; } = Array.Empty<string>();

        [JsonPropertyName("exclude")]
        [ConfigurationKeyName("exclude")]
        public string[] Exclude { get; set; } = Array.Empty<string>();

        [JsonPropertyName("workingDirectory")]
        [ConfigurationKeyName("workingDirectory")]
        public string? WorkingDirectory { get; set; }

        [JsonPropertyName("encoding")]
        [ConfigurationKeyName("encoding")]
        public string Encoding { get; set; } = "utf-8";

        [Range(100, int.MaxValue)]
        [JsonPropertyName("globMinimumCooldownMs")]
        [ConfigurationKeyName("globMinimumCooldownMs")]
        public int GlobMinimumCooldownMs { get; set; } = 1000;

        [Range(1024, int.MaxValue)]
        [JsonPropertyName("readBufferSize")]
        [ConfigurationKeyName("readBufferSize")]
        public int ReadBufferSize { get; set; } = 8192;

        [JsonPropertyName("startAtBeginning")]
        [ConfigurationKeyName("startAtBeginning")]
        public bool StartAtBeginning { get; set; } = false;

        [JsonPropertyName("ignoreCheckpoints")]
        [ConfigurationKeyName("ignoreCheckpoints")]
        public bool IgnoreCheckpoints { get; set; } = false;

        [JsonPropertyName("multiline")]
        [ConfigurationKeyName("multiline")]
        public MultilineConfiguration? Multiline { get; set; }

        [Range(1024, int.MaxValue)]
        [JsonPropertyName("maxLineBytes")]
        [ConfigurationKeyName("maxLineBytes")]
        public int MaxLineBytes { get; set; } = 1_048_576;

        [Range(1, int.MaxValue)]
        [JsonPropertyName("ignoreOlderSecs")]
        [ConfigurationKeyName("ignoreOlderSecs")]
        public int? IgnoreOlderSecs { get; set; }
    }

    public sealed class MultilineConfiguration
    {
        [JsonPropertyName("startPattern")]
        [ConfigurationKeyName("startPattern")]
        public string? StartPattern { get; set; }

        [JsonPropertyName("conditionPattern")]
        [ConfigurationKeyName("conditionPattern")]
        public string? ConditionPattern { get; set; }

        [JsonPropertyName("mode")]
        [ConfigurationKeyName("mode")]
        public string Mode { get; set; } = "startPattern";

        [JsonPropertyName("timeoutMs")]
        [ConfigurationKeyName("timeoutMs")]
        public int TimeoutMs { get; set; } = 1000;
    }

    internal sealed record CheckpointInfo(string FilePath, long Position, long FileSize, DateTime LastModified);

    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(LogFileServiceConfiguration))]
    [JsonSerializable(typeof(MultilineConfiguration))]
    [JsonSerializable(typeof(Dictionary<string, object>))]
    [JsonSerializable(typeof(CheckpointInfo))]
    internal sealed partial class LogFileSourceGenerationContext : JsonSerializerContext
    {
    }
}
