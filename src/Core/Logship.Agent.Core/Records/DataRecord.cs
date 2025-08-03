// <copyright file="DataRecord.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

namespace Logship.Agent.Core.Records
{
    public sealed record DataRecord(string Schema, DateTimeOffset TimeStamp, Dictionary<string, object> Data)
    {
    }
}

