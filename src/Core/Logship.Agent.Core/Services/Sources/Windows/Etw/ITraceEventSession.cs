// <copyright file="ITraceEventSession.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Records;
using Microsoft.Diagnostics.Tracing;

namespace Logship.Agent.Core.Services.Sources.Windows.Etw
{
    public interface ITraceEventSession : IDisposable
    {
        void EnableProvider(Guid providerGuid, TraceEventLevel maximumEventLevel, ulong enabledKeywords);
        void DisableProvider(Guid providerGuid);
        void EnableProvider(string providerName, TraceEventLevel maximumEventLevel, ulong enabledKeywordsOptions);
        void DisableProvider(string providerName);
        void Process(Action<DataRecord> onEvent, CancellationToken token);
    }
}

