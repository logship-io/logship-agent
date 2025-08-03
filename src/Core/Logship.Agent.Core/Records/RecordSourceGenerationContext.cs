// <copyright file="RecordSourceGenerationContext.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using System.Text.Json.Serialization;

namespace Logship.Agent.Core.Records
{
    [JsonSerializable(typeof(DataRecord))]
    [JsonSerializable(typeof(IEnumerable<DataRecord>))]
    [JsonSerializable(typeof(IReadOnlyCollection<DataRecord>))]
    [JsonSerializable(typeof(long))]
    [JsonSerializable(typeof(ulong))]
    [JsonSerializable(typeof(int))]
    [JsonSerializable(typeof(uint))]
    [JsonSerializable(typeof(float))]
    [JsonSerializable(typeof(double))]
    [JsonSerializable(typeof(decimal))]
    [JsonSerializable(typeof(Guid))]
    [JsonSerializable(typeof(TimeSpan))]
    [JsonSerializable(typeof(DateTime))]
    [JsonSerializable(typeof(DateTimeOffset))]
    [JsonSerializable(typeof(bool))]
    internal sealed partial class RecordSourceGenerationContext : JsonSerializerContext
    {
    }
}

