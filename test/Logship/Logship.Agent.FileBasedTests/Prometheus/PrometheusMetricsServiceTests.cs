// <copyright file="PrometheusMetricsServiceTests.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Logship.Agent.Core.Records;
using Logship.Agent.Core.Services.Sources.Common.Prometheus;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Text;

namespace Logship.Agent.FileBasedTests.Prometheus
{
    [TestClass]
    public sealed class PrometheusMetricsServiceTests
    {
        private const string Payload = "# TYPE http_requests_total counter\n"
            + "http_requests_total{method=\"post\",code=\"200\"} 1027\n"
            + "http_request_duration_seconds_bucket{le=\"+Inf\"} 144320\n"
            + "some_nan_metric NaN\n";

        [TestMethod]
        [Timeout(15000)]
        public async Task ScrapesEndpointAndEmitsFlattenedRecords()
        {
            using var listener = StartListener(out var url);
            var buffer = new CaptureBuffer();
            var config = new SourcesConfiguration
            {
                Prometheus = new PrometheusConfiguration
                {
                    Targets = { new PrometheusTargetConfiguration { Endpoint = url, Interval = TimeSpan.FromMilliseconds(250) } },
                },
            };

            var service = new PrometheusMetricsService(Options.Create(config), buffer, new TestHttpClientFactory(), NullLogger<PrometheusMetricsService>.Instance);
            await service.StartAsync(CancellationToken.None);
            try
            {
                while (buffer.Snapshot().Count == 0)
                {
                    await Task.Delay(50);
                }
            }
            finally
            {
                await service.StopAsync(CancellationToken.None);
            }

            var records = buffer.Snapshot();
            Assert.IsTrue(records.All(r => r.Schema == "Prometheus.Metrics"));
            Assert.IsFalse(records.Any(r => (string)r.Data["name"] == "some_nan_metric"));

            var request = records.First(r => (string)r.Data["name"] == "http_requests_total");
            Assert.AreEqual(1027d, request.Data["value"]);
            Assert.AreEqual("post", request.Data["method"]);
            Assert.AreEqual("200", request.Data["code"]);
            Assert.AreEqual(url, request.Data["endpoint"]);

            var bucket = records.First(r => (string)r.Data["name"] == "http_request_duration_seconds_bucket");
            Assert.AreEqual("+Inf", bucket.Data["le"]);
            Assert.AreEqual(144320d, bucket.Data["value"]);
        }

        [TestMethod]
        [Timeout(15000)]
        public async Task SendsConfiguredHeadersWithScrape()
        {
            string? authorization = null;
            using var listener = StartListener(out var url, request => authorization = request.Headers["Authorization"]);
            var buffer = new CaptureBuffer();
            var config = new SourcesConfiguration
            {
                Prometheus = new PrometheusConfiguration
                {
                    Targets =
                    {
                        new PrometheusTargetConfiguration
                        {
                            Endpoint = url,
                            Interval = TimeSpan.FromMilliseconds(250),
                            Headers = { ["Authorization"] = "Bearer test-token" },
                        },
                    },
                },
            };

            var service = new PrometheusMetricsService(Options.Create(config), buffer, new TestHttpClientFactory(), NullLogger<PrometheusMetricsService>.Instance);
            await service.StartAsync(CancellationToken.None);
            try
            {
                while (buffer.Snapshot().Count == 0)
                {
                    await Task.Delay(50);
                }
            }
            finally
            {
                await service.StopAsync(CancellationToken.None);
            }

            Assert.AreEqual("Bearer test-token", authorization);
        }

        private static HttpListener StartListener(out string url, Action<HttpListenerRequest>? onRequest = null)
        {
            for (int attempt = 0; ; attempt++)
            {
                int port = Random.Shared.Next(20000, 60000);
                var listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{port}/metrics/");
                try
                {
                    listener.Start();
                }
                catch (HttpListenerException) when (attempt < 5)
                {
                    continue;
                }

                url = $"http://127.0.0.1:{port}/metrics/";
                _ = Task.Run(async () =>
                {
                    try
                    {
                        while (listener.IsListening)
                        {
                            var context = await listener.GetContextAsync();
                            onRequest?.Invoke(context.Request);
                            var bytes = Encoding.UTF8.GetBytes(Payload);
                            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
                            context.Response.Close();
                        }
                    }
                    catch
                    {
                        // Listener closed.
                    }
                });

                return listener;
            }
        }

        private sealed class CaptureBuffer : IEventBuffer
        {
            private readonly object mutex = new object();
            private readonly List<DataRecord> records = new List<DataRecord>();

            public void Add(DataRecord data)
            {
                lock (this.mutex)
                {
                    this.records.Add(data);
                }
            }

            public void Add(IReadOnlyCollection<DataRecord> data)
            {
                lock (this.mutex)
                {
                    this.records.AddRange(data);
                }
            }

            public Task<IReadOnlyCollection<DataRecord>> NextAsync(CancellationToken token)
            {
                return Task.FromResult<IReadOnlyCollection<DataRecord>>(Array.Empty<DataRecord>());
            }

            public List<DataRecord> Snapshot()
            {
                lock (this.mutex)
                {
                    return new List<DataRecord>(this.records);
                }
            }
        }

        private sealed class TestHttpClientFactory : IHttpClientFactory
        {
            public HttpClient CreateClient(string name)
            {
                return new HttpClient();
            }
        }
    }
}
