// <copyright file="IEventBuffer.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Internals;
using Logship.Agent.Core.Records;

namespace Logship.Agent.Core.Events
{
    public interface IEventBuffer
    {
        void Add(DataRecord data);
        void Add(IReadOnlyCollection<DataRecord> data);
        Task<IReadOnlyCollection<DataRecord>> NextAsync(CancellationToken token);
    }
}

