﻿using Logship.Agent.Core.Internals;
using Logship.Agent.Core.Records;

namespace Logship.Agent.Core.Events
{
    public interface IEventSink : IEventBuffer, IEventOutput
    {
        Task FlushAsync(CancellationToken cancellationToken);
    }
}
