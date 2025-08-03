// <copyright file="SourceGenerationContext.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using System.Text.Json.Serialization;

namespace Logship.Agent.Core.Configuration
{
    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(SourcesConfiguration))]
    [JsonSerializable(typeof(OutputConfiguration))]
    internal sealed partial class SourceGenerationContext : JsonSerializerContext
    {
    }
}

