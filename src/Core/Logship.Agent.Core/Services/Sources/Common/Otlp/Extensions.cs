// <copyright file="Extensions.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Google.Protobuf.WellKnownTypes;
using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Internals;
using Microsoft.AspNetCore.DataProtection.KeyManagement;

namespace Logship.Agent.Core.Services.Sources.Common.Otlp
{
    internal static class Extensions
    {

        internal static Dictionary<string, object> CreateAttributeDictionary(this IReadOnlyCollection<OpenTelemetry.Proto.Common.V1.KeyValue> attributes)
        {
            var dict = new Dictionary<string, object>();
            foreach (var kvp in attributes)
            {
                string key = kvp.Key;
                switch (kvp.Value.ValueCase)
                {
                    case OpenTelemetry.Proto.Common.V1.AnyValue.ValueOneofCase.StringValue:
                        dict[key] = kvp.Value.StringValue;
                        break;
                    case OpenTelemetry.Proto.Common.V1.AnyValue.ValueOneofCase.BoolValue:
                        dict[key] = kvp.Value.BoolValue;
                        break;
                    case OpenTelemetry.Proto.Common.V1.AnyValue.ValueOneofCase.IntValue:
                        dict[key] = kvp.Value.IntValue;
                        break;
                    case OpenTelemetry.Proto.Common.V1.AnyValue.ValueOneofCase.DoubleValue:
                        dict[key] = kvp.Value.DoubleValue;
                        break;
                    case OpenTelemetry.Proto.Common.V1.AnyValue.ValueOneofCase.KvlistValue:
                        dict[key] = CreateAttributeDictionary(kvp.Value.KvlistValue.Values);
                        break;
                    case OpenTelemetry.Proto.Common.V1.AnyValue.ValueOneofCase.None:
                    case OpenTelemetry.Proto.Common.V1.AnyValue.ValueOneofCase.ArrayValue:
                    case OpenTelemetry.Proto.Common.V1.AnyValue.ValueOneofCase.BytesValue:
                    default:
                        break;
                }
            }

            return dict;
        }

        internal static void AddExtractedAttributes(this Dictionary<string, object> attributes, IReadOnlyDictionary<string, ExtractResourceAttributeValue> extractResourceAttributes)
        {
            foreach (var kvp in extractResourceAttributes)
            {
                if (attributes.ContainsKey(kvp.Key))
                {
                    continue;
                }

                attributes[kvp.Key] = kvp.Value.DefaultValue;
            }

        }
    }
}

