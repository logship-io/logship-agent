// <copyright file="StandardTraceEventSession.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Diagnostics.Tracing;
using Logship.Agent.Core.Records;
using Logship.Agent.Core.Services.Sources.Windows.Etw;

namespace Logship.Agent.Core.Services.Sources.Windows.Etw
{
    internal sealed class StandardTraceEventSession : ITraceEventSession
    {
        private TraceEventSession? inner;
        private volatile bool isProcessing;

        public StandardTraceEventSession(string sessionNamePrefix, bool cleanupOldSessions, bool reuseExisting)
        {
            string? sessionName = null;
            if (cleanupOldSessions)
            {
                sessionName = CleanupMatchingSessions(sessionNamePrefix, reuseExisting);
            }

            sessionName ??= $"{sessionNamePrefix}-{Guid.NewGuid()}";

            // even if the session already exists, we must restart it as we cannot enable providers on an attached session
            inner = new TraceEventSession(sessionName, TraceEventSessionOptions.Create);
            isProcessing = false;
        }

        public void DisableProvider(Guid providerGuid)
        {
            ObjectDisposedException.ThrowIf(inner == null, inner);
            inner.DisableProvider(providerGuid);
        }

        public void DisableProvider(string providerName)
        {
            ObjectDisposedException.ThrowIf(inner == null, inner);
            inner.DisableProvider(providerName);
        }

        public void Dispose()
        {
            if (inner != null)
            {
                // TraceEventSession.StopOnDispose is true by default so there is no need to Stop() the session explicitly.
                inner.Dispose();
                inner = null;
            }
        }

        public void EnableProvider(Guid providerGuid, TraceEventLevel maximumEventLevel, ulong enabledKeywords)
        {
            ObjectDisposedException.ThrowIf(inner == null, inner);
            inner.EnableProvider(providerGuid, maximumEventLevel, enabledKeywords);
        }

        public void EnableProvider(string providerName, TraceEventLevel maximumEventLevel, ulong enabledKeywords)
        {
            ObjectDisposedException.ThrowIf(inner == null, inner);
            inner.EnableProvider(providerName, maximumEventLevel, enabledKeywords);
        }

        public void Process(Action<DataRecord> onEvent, CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(inner == null, inner);
            ArgumentNullException.ThrowIfNull(onEvent, nameof(onEvent));

            if (!isProcessing)
            {
                isProcessing = true;
                inner.Source.Dynamic.All += (traceEvent) =>
                {
                    // Suppress events from TplEventSource--they are mostly interesting for debugging task processing and interaction,
                    // and not that useful for production tracing. However, TPL EventSource must be enabled to get hierarchical activity IDs.
                    if (!TraceEventExtensions.TplEventSourceGuid.Equals(traceEvent.ProviderGuid))
                    {
                        onEvent(traceEvent.ToEventData());
                    }
                };

                token.Register(() =>
                {
                    inner.Stop();
                });
                inner.Source.Process();
            }
        }

        private static string? CleanupMatchingSessions(string sessionNamePrefix, bool keepOne)
        {
            // TODO: This function makes absolutely no sense. It should be rewritten.

            string? result = null;

            foreach (var sesName in TraceEventSession.GetActiveSessionNames())
            {
                if (!sesName.StartsWith(sessionNamePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var session = TraceEventSession.GetActiveSession(sesName);
                if (session == null || !session.IsRealTime)
                {
                    continue;
                }

                if (keepOne && result == null)
                {
                    result = session.SessionName;
                }
                else
                {
                    session.Dispose();
                }
            }

            return result;
        }
    }
}

