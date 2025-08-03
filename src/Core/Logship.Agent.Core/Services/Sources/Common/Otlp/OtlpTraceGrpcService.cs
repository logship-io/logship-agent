// <copyright file="OtlpTraceGrpcService.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Grpc.Core;
using Logship.Agent.Core.Events;
using Logship.Agent.Core.Inputs.Common.Udp;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Trace.V1;
using System.Text.Json;
using System.Text;

namespace Logship.Agent.Core.Services.Sources.Common.Otlp
{
    public sealed class OtlpTraceGrpcService : TraceService.TraceServiceBase
    {
        private readonly IEventBuffer sink;
        private readonly ILogger<OtlpTraceGrpcService> logger;
        private readonly IReadOnlyDictionary<string, ExtractResourceAttributeValue> extractResourceAttributes;

        private static readonly ExportTraceServiceResponse StaticResponse = new ExportTraceServiceResponse();

        public OtlpTraceGrpcService(IReadOnlyDictionary<string, ExtractResourceAttributeValue> extractResourceAttributes, IEventBuffer sink, ILogger<OtlpTraceGrpcService> logger)
        {
            this.sink = sink;
            this.logger = logger;
            this.extractResourceAttributes = extractResourceAttributes;
        }

        public override async Task<ExportTraceServiceResponse> Export(ExportTraceServiceRequest request, ServerCallContext context)
        {
            Log.TraceMessage(logger);
            foreach (var resource in request.ResourceSpans)
            {
                var rd = new Dictionary<string, object>();
                rd.AddExtractedAttributes(this.extractResourceAttributes);

                using (var stream = new MemoryStream(resource.Resource.Attributes.Count * 100))
                {
                    var resourceAttributes = Extensions.CreateAttributeDictionary(resource.Resource.Attributes);
                    await JsonSerializer.SerializeAsync(stream, resourceAttributes, OtlpAttributesSerializationContext.Default.DictionaryStringObject, context.CancellationToken);
                    await stream.FlushAsync(context.CancellationToken);
                    rd["ResourceAttributes"] = Encoding.UTF8.GetString(stream.ToArray());
                }
                rd["ResourceSchemaUrl"] = resource.SchemaUrl;

                foreach (var scope in resource.ScopeSpans)
                {
                    var sd = new Dictionary<string, object>(rd);
                    using (var stream = new MemoryStream(scope.Scope.Attributes.Count * 100))
                    {
                        var scopeAttributes = Extensions.CreateAttributeDictionary(scope.Scope.Attributes);
                        await JsonSerializer.SerializeAsync(stream, scopeAttributes, OtlpAttributesSerializationContext.Default.DictionaryStringObject, context.CancellationToken);
                        await stream.FlushAsync(context.CancellationToken);

                        sd["ScopeAttributes"] = Encoding.UTF8.GetString(stream.ToArray());
                    }
                    sd["ScopeName"] = scope.Scope.Name;
                    sd["ScopeDroppedAttributesCount"] = scope.Scope.DroppedAttributesCount;
                    sd["ScopeVersion"] = scope.Scope.Version;
                    sd["ScopeSchemaUrl"] = scope.SchemaUrl;

                    foreach (var span in scope.Spans)
                    {
                        var ssd = new Dictionary<string, object>(sd);
                        using (var stream = new MemoryStream(span.Attributes.Count * 100))
                        {
                            var spanAttributes = Extensions.CreateAttributeDictionary(span.Attributes);
                            await JsonSerializer.SerializeAsync(stream, spanAttributes, OtlpAttributesSerializationContext.Default.DictionaryStringObject, context.CancellationToken);
                            await stream.FlushAsync(context.CancellationToken);
                            ssd["Attributes"] = Encoding.UTF8.GetString(stream.ToArray());
                        }

                        ssd["Name"] = span.Name;
                        ssd["StatusCode"] = (int)span.Status.Code;
                        ssd["StatusMessage"] = span.Status.Message;
                        ssd["Flags"] = span.Flags;
                        ssd["Kind"] = (int)span.Kind;
                        ssd["SpanId"] = span.SpanId.ToBase64();
                        ssd["ParentSpanId"] = span.ParentSpanId.ToBase64();
                        ssd["TraceId"] = span.TraceId.ToBase64();
                        ssd["TraceState"] = span.TraceState;
                        ssd["EndTimeUnixNano"] = span.EndTimeUnixNano;
                        ssd["StartTimeUnixNano"] = span.StartTimeUnixNano;

                        var dataRecord = new Records.DataRecord(Span.Descriptor.FullName, DateTimeOffset.FromUnixTimeMilliseconds((long)(span.StartTimeUnixNano / 1000000L)), ssd);
                        this.sink.Add(dataRecord);
                    }
                }
            }

            return StaticResponse;
        }

        
    }

    internal static partial class Log
    {
        [LoggerMessage(LogLevel.Debug, "Received OTLP Trace Message.")]
        public static partial void TraceMessage(ILogger logger);
    }
}

