// <copyright file="OtlpLogsGrpcService.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Grpc.Core;
using Logship.Agent.Core.Events;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Logs.V1;
using System.Text.Json;
using System.Text;
using Logship.Agent.Core.Inputs.Common.Udp;

namespace Logship.Agent.Core.Services.Sources.Common.Otlp
{
    public sealed class OtlpLogsGrpcService : LogsService.LogsServiceBase
    {
        private readonly IEventBuffer sink;
        private readonly ILogger<OtlpLogsGrpcService> logger;
        private readonly IReadOnlyDictionary<string, ExtractResourceAttributeValue> extractResourceAttributes;

        private static readonly ExportLogsServiceResponse StaticResponse = new ExportLogsServiceResponse();

        public OtlpLogsGrpcService(IReadOnlyDictionary<string, ExtractResourceAttributeValue> extractResourceAttributes, IEventBuffer sink, ILogger<OtlpLogsGrpcService> logger)
        {
            this.sink = sink;
            this.logger = logger;
            this.extractResourceAttributes = extractResourceAttributes;
        }

        public override async Task<ExportLogsServiceResponse> Export(ExportLogsServiceRequest request, ServerCallContext context)
        {
            Log.LogsMessage(logger);
            foreach (var resource in request.ResourceLogs)
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

                rd["ResourceDroppedAttributesCount"] = resource.Resource.DroppedAttributesCount;


                foreach (var scope in resource.ScopeLogs)
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

                    foreach (var log in scope.LogRecords)
                    {
                        var slr = new Dictionary<string, object>(sd);
                        using (var stream = new MemoryStream(log.Attributes.Count * 100))
                        {
                            var logAttributes = Extensions.CreateAttributeDictionary(log.Attributes);
                            await JsonSerializer.SerializeAsync(stream, logAttributes, OtlpAttributesSerializationContext.Default.DictionaryStringObject, context.CancellationToken);
                            await stream.FlushAsync(context.CancellationToken);
                            slr["Attributes"] = Encoding.UTF8.GetString(stream.ToArray());
                        }

                        slr["Body"] = log.Body.HasStringValue ? log.Body.StringValue : string.Empty;
                        slr["Flags"] = log.Flags;
                        slr["SeverityText"] = log.SeverityText;
                        slr["SeverityNumber"] = (int)log.SeverityNumber;
                        slr["SpanId"] = log.SpanId.ToBase64();
                        slr["ObservedTimeUnixNano"] = log.ObservedTimeUnixNano;
                        slr["TimeUnixNano"] = log.TimeUnixNano;

                       this.sink.Add(new Records.DataRecord(LogRecord.Descriptor.FullName, DateTimeOffset.FromUnixTimeMilliseconds((long)(log.TimeUnixNano / 1000000L)), slr));
                    }
                }
            }

            return StaticResponse;
        }
    }

    internal static partial class Log
    {
        [LoggerMessage(LogLevel.Debug, "Received OTLP Logs Message.")]
        public static partial void LogsMessage(ILogger logger);

    }
}

