// <copyright file="OtlpAttributes.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using System.Text.Json.Serialization;

namespace Logship.Agent.Core.Inputs.Common.Udp
{
    [JsonSerializable(typeof(Dictionary<string, object>))]
    [JsonSerializable(typeof(long))]
    [JsonSerializable(typeof(ulong))]
    [JsonSerializable(typeof(int))]
    [JsonSerializable(typeof(uint))]
    [JsonSerializable(typeof(float))]
    [JsonSerializable(typeof(double))]
    [JsonSerializable(typeof(Guid))]
    [JsonSerializable(typeof(TimeSpan))]
    [JsonSerializable(typeof(DateTime))]
    [JsonSerializable(typeof(DateTimeOffset))]
    [JsonSerializable(typeof(bool))]
    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
    public sealed partial class OtlpAttributesSerializationContext : JsonSerializerContext
    {
    }
}

