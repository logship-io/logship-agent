// <copyright file="TraceEventExtensions.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Records;
using Microsoft.Diagnostics.Tracing;
using System.Text;

namespace Logship.Agent.Core.Services.Sources.Windows.Etw
{
    internal static class TraceEventExtensions
    {
        public static readonly Guid TplEventSourceGuid = new Guid("2e5dba47-a3d2-4d16-8ee0-6671ffdcd7b5");
        public static readonly ulong TaskFlowActivityIdsKeyword = 0x80;

        public static DataRecord ToEventData(this TraceEvent traceEvent)
        {
            string name = traceEvent.ProviderName;
            if (string.IsNullOrEmpty(name))
            {
                name = traceEvent.ProviderGuid.ToString();
            }
            var eventData = new DataRecord(CleanEventName($"windows.etw.{name}.{traceEvent.EventName}"), traceEvent.TimeStamp.ToUniversalTime(), new Dictionary<string, object>()
            {
                { "machine", Environment.MachineName },
                { nameof(traceEvent.ProviderName), traceEvent.ProviderName },
                { nameof(traceEvent.ProviderGuid), traceEvent.ProviderGuid },
                { "TraceEventProcessName", traceEvent.ProcessName },
                { "TraceEventProcessID", traceEvent.ProcessID },
                { nameof(traceEvent.ID), (int)traceEvent.ID },
                { nameof(traceEvent.EventName), traceEvent.EventName },
            });

            if (traceEvent.ActivityID != default(Guid))
            {
                eventData.Data.Add(nameof(traceEvent.ActivityID), ActivityPathDecoder.GetActivityPathString(traceEvent.ActivityID));
            }
            if (traceEvent.RelatedActivityID != default(Guid))
            {
                eventData.Data.Add(nameof(traceEvent.RelatedActivityID), traceEvent.RelatedActivityID.ToString());
            }

            try
            {
                // If the event has a badly formatted manifest, the FormattedMessage property getter might throw
                string message = traceEvent.FormattedMessage;
                if (message != null)
                {
                    eventData.Data.Add("Message", traceEvent.FormattedMessage);
                }
            }
            catch { }

            bool hasPayload = traceEvent.PayloadNames != null && traceEvent.PayloadNames.Length > 0;
            if (hasPayload)
            {
                for (int i = 0; i < traceEvent.PayloadNames!.Length; i++)
                {
                    try
                    {
                        var payloadName = traceEvent.PayloadNames[i];
                        var payloadValue = traceEvent.PayloadValue(i);
                        eventData.Data.Add(payloadName, payloadValue);
                    }
                    catch { }
                }
            }

            return eventData;
        }

        private static string CleanEventName(string eventName)
        {
            var wasChanges = false;

            var builder = new StringBuilder(eventName);
            for (var i = 0; i < builder.Length; i++)
            {
                if (char.IsLetterOrDigit(builder[i]))
                {
                    continue;
                }
                if (builder[i] == '.' || builder[i] == '-' || builder[i] == '_')
                {
                    continue;
                }
                builder[i] = '_';
                wasChanges = true;
            }

            if (!wasChanges)
            {
                return eventName;
            }

            return builder.ToString();
        }
    }
}

