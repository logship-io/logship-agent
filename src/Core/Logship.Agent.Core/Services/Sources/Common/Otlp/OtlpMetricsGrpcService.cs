// <copyright file="OtlpMetricsGrpcService.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Grpc.Core;
using Logship.Agent.Core.Events;
using Logship.Agent.Core.Inputs.Common.Udp;
using Logship.Agent.Core.Internals;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Metrics.V1;
using System.Text.Json;
using System.Text;

namespace Logship.Agent.Core.Services.Sources.Common.Otlp
{
    public sealed class OtlpMetricsGrpcService : MetricsService.MetricsServiceBase
    {
        private readonly IEventBuffer sink;
        private readonly ILogger<OtlpMetricsGrpcService> logger;
        private readonly IReadOnlyDictionary<string, ExtractResourceAttributeValue> extractResourceAttributes;

        private static readonly ExportMetricsServiceResponse StaticResponse = new ExportMetricsServiceResponse();

        public OtlpMetricsGrpcService(IReadOnlyDictionary<string, ExtractResourceAttributeValue> extractResourceAttributes, IEventBuffer sink, ILogger<OtlpMetricsGrpcService> logger)
        {
            this.sink = sink;
            this.logger = logger;
            this.extractResourceAttributes = extractResourceAttributes;
        }

        public override async Task<ExportMetricsServiceResponse> Export(ExportMetricsServiceRequest request, ServerCallContext context)
        {
            Log.MetricsMessage(this.logger);
            for (int i = 0; i < request.ResourceMetrics.Count; i++)
            {
                var resource = request.ResourceMetrics[i];
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
                

                for (int j = 0; j < resource.ScopeMetrics.Count; j++)
                {
                    var scope = resource.ScopeMetrics[j];
                    var sd = new Dictionary<string, object>();
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

                    for (int k = 0; k < scope.Metrics.Count; k++)
                    {
                        var metric = scope.Metrics[k];
                        var schema = metric.Name.AsSpan().CleanSchemaName(allowPeriod: true);
                        switch (metric.DataCase)
                        {
                            case OpenTelemetry.Proto.Metrics.V1.Metric.DataOneofCase.Gauge:
                                {
                                    foreach (var dp in metric.Gauge.DataPoints)
                                    {
                                        Dictionary<string, object> dict = BaseMetricAttributes(metric);
                                        using (var stream = new MemoryStream(dp.Attributes.Count * 100))
                                        {
                                            var metricAttributes = Extensions.CreateAttributeDictionary(dp.Attributes);
                                            await JsonSerializer.SerializeAsync(stream, metricAttributes, OtlpAttributesSerializationContext.Default.DictionaryStringObject, context.CancellationToken);
                                            await stream.FlushAsync(context.CancellationToken);
                                            dict["Attributes"] = Encoding.UTF8.GetString(stream.ToArray());
                                        }

                                        AddNumericDataPoint(dp, dict);
                                        this.sink.Add(new Records.DataRecord(schema, DateTimeOffset.UtcNow, dict));
                                    }
                                    break;
                                }
                            case OpenTelemetry.Proto.Metrics.V1.Metric.DataOneofCase.Sum:
                                {
                                    var sum = metric.Sum;
                                    foreach (var dp in metric.Sum.DataPoints)
                                    {
                                        Dictionary<string, object> dict = BaseMetricAttributes(metric);
                                        using (var stream = new MemoryStream(dp.Attributes.Count * 100))
                                        {
                                            var metricAttributes = Extensions.CreateAttributeDictionary(dp.Attributes);
                                            await JsonSerializer.SerializeAsync(stream, metricAttributes, OtlpAttributesSerializationContext.Default.DictionaryStringObject, context.CancellationToken);
                                            await stream.FlushAsync(context.CancellationToken);
                                            dict["Attributes"] = Encoding.UTF8.GetString(stream.ToArray());
                                        }

                                        AddNumericDataPoint(dp, dict);
                                        this.sink.Add(new Records.DataRecord(schema, DateTimeOffset.UtcNow, dict));
                                    }
                                    break;
                                }
                            case OpenTelemetry.Proto.Metrics.V1.Metric.DataOneofCase.Histogram:
                                {
                                    foreach (var dp in metric.Histogram.DataPoints)
                                    {
                                        Dictionary<string, object> dict = BaseMetricAttributes(metric);
                                        dict["AggregationTemporality"] = metric.Histogram.AggregationTemporality.ToString("g");
                                        dict["Min"] = dp.Min;
                                        dict["Sum"] = dp.Sum;
                                        dict["Max"] = dp.Max;
                                        dict["Flags"] = dp.Flags;
                                        dict["StartTimeUnixNano"] = dp.StartTimeUnixNano;
                                        dict["TimeUnixNano"] = dp.TimeUnixNano;
                                        using (var stream = new MemoryStream(dp.Attributes.Count * 100))
                                        {
                                            var metricAttributes = Extensions.CreateAttributeDictionary(dp.Attributes);
                                            await JsonSerializer.SerializeAsync(stream, metricAttributes, OtlpAttributesSerializationContext.Default.DictionaryStringObject, context.CancellationToken);
                                            await stream.FlushAsync(context.CancellationToken);
                                            dict["Attributes"] = Encoding.UTF8.GetString(stream.ToArray());
                                        }

                                        for (int b = 0; b < dp.BucketCounts.Count; b++)
                                        {
                                            var clone = new Dictionary<string, object>(dict);
                                            ulong bucket = dp.BucketCounts[b];
                                            if (b == 0)
                                            {
                                                clone["BucketMin"] = double.MinValue;
                                                clone["BucketMax"] = dp.ExplicitBounds[b];
                                            }
                                            else if (b == dp.BucketCounts.Count - 1)
                                            {
                                                clone["BucketMin"] = dp.ExplicitBounds[^1];
                                                clone["BucketMax"] = double.MaxValue;
                                            }
                                            else
                                            {
                                                clone["BucketMin"] = dp.ExplicitBounds[b - 1];
                                                clone["BucketMax"] = dp.ExplicitBounds[b];
                                            }

                                            clone["BucketCount"] = bucket;
                                            this.sink.Add(new Records.DataRecord(schema, DateTimeOffset.UtcNow, clone));
                                        }
                                    }
                                    break;
                                }
                            case OpenTelemetry.Proto.Metrics.V1.Metric.DataOneofCase.ExponentialHistogram:
                                {
                                    foreach (var dp in metric.ExponentialHistogram.DataPoints)
                                    {
                                        Dictionary<string, object> dict = BaseMetricAttributes(metric);
                                        long histobase = 2L << (2 << (-dp.Scale));

                                        dict["Scale"] = dp.Scale;
                                        dict["Base"] = histobase;

                                        dict["Min"] = dp.Min;
                                        dict["Sum"] = dp.Sum;
                                        dict["Max"] = dp.Max;
                                        dict["Flags"] = dp.Flags;
                                        dict["StartTimeUnixNano"] = dp.StartTimeUnixNano;
                                        dict["TimeUnixNano"] = dp.TimeUnixNano;
                                        dict["PositiveOffset"] = dp.Positive.Offset;
                                        dict["NegativeOffset"] = dp.Negative.Offset;
                                        using (var stream = new MemoryStream(dp.Attributes.Count * 100))
                                        {
                                            var metricAttributes = Extensions.CreateAttributeDictionary(dp.Attributes);
                                            await JsonSerializer.SerializeAsync(stream, metricAttributes, OtlpAttributesSerializationContext.Default.DictionaryStringObject, context.CancellationToken);
                                            await stream.FlushAsync(context.CancellationToken);
                                            dict["Attributes"] = Encoding.UTF8.GetString(stream.ToArray());
                                        }

                                        for (int b = 0; b < dp.Positive.BucketCounts.Count; b++)
                                        {
                                            var clone = new Dictionary<string, object>(dict);
                                            ulong bucket = dp.Positive.BucketCounts[b];
                                            clone["BucketMin"] = Math.Pow(histobase, b);
                                            clone["BucketMax"] = Math.Pow(histobase, b + 1);
                                            clone["BucketCount"] = bucket;
                                            this.sink.Add(new Records.DataRecord(schema, DateTimeOffset.UtcNow, clone));
                                        }

                                        for (int b = 0; b < dp.Negative.BucketCounts.Count; b++)
                                        {
                                            var clone = new Dictionary<string, object>(dict);
                                            ulong bucket = dp.Negative.BucketCounts[b];
                                            clone["BucketMin"] = -Math.Pow(histobase, b + 1);
                                            clone["BucketMax"] = -Math.Pow(histobase, b);
                                            clone["BucketCount"] = bucket;
                                            this.sink.Add(new Records.DataRecord(schema, DateTimeOffset.UtcNow, clone));
                                        }
                                    }
                                    break;
                                }
                            case OpenTelemetry.Proto.Metrics.V1.Metric.DataOneofCase.Summary:
                            case OpenTelemetry.Proto.Metrics.V1.Metric.DataOneofCase.None:
                            default:
                                break;
                        }
                    }
                }
            }

            return StaticResponse;
        }

        private static void AddNumericDataPoint(NumberDataPoint? dp, Dictionary<string, object> dict)
        {
            if (dp == null)
            {
                return;
            }

            if (dp.HasAsDouble)
            {
                dict["Value"] = dp.AsDouble;
            }
            else if (dp.HasAsInt)
            {
                dict["Value"] = dp.AsInt;
            }
            else
            {
                dict["Value"] = 0;
            }
        }

        private static Dictionary<string, object> BaseMetricAttributes(Metric metric)
        {
            var d = new Dictionary<string, object>()
            {
                ["machine"] = Environment.MachineName,
                ["Description"] = metric.Description,
                ["Unit"] = metric.Unit,
                ["DataCase"] = metric.DataCase.ToString("g"),
            };

            return d;
        }
    }

    internal static partial class Log
    {
        [LoggerMessage(LogLevel.Debug, "Received OTLP Metrics Message.")]
        public static partial void MetricsMessage(ILogger logger);
    }
}

