// <copyright file="PrometheusTextParserTests.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Services.Sources.Common.Prometheus;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text;

namespace Logship.Agent.FileBasedTests.Prometheus
{
    [TestClass]
    public sealed class PrometheusTextParserTests
    {
        private static async Task<PrometheusParseResult> ParseAsync(string text)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
            return await PrometheusTextParser.ParseAsync(stream, CancellationToken.None);
        }

        [TestMethod]
        public async Task ParsesUnlabeledSampleWithScientificNotation()
        {
            var result = await ParseAsync("node_boot_time_seconds 1.7560473e+09");

            Assert.AreEqual(0, result.MalformedLines);
            Assert.AreEqual(1, result.Samples.Count);
            Assert.AreEqual("node_boot_time_seconds", result.Samples[0].Name);
            Assert.AreEqual(0, result.Samples[0].Labels.Count);
            Assert.AreEqual(1.7560473e+09, result.Samples[0].Value);
            Assert.IsNull(result.Samples[0].Timestamp);
        }

        [TestMethod]
        public async Task ParsesNumericValueFormats()
        {
            var text = string.Join('\n',
                "a 42",
                "b -7",
                "c +1.5",
                "d -2e-3",
                "e .5");

            var result = await ParseAsync(text);

            Assert.AreEqual(0, result.MalformedLines);
            Assert.AreEqual(5, result.Samples.Count);
            Assert.AreEqual(42d, result.Samples[0].Value);
            Assert.AreEqual(-7d, result.Samples[1].Value);
            Assert.AreEqual(1.5, result.Samples[2].Value);
            Assert.AreEqual(-2e-3, result.Samples[3].Value);
            Assert.AreEqual(0.5, result.Samples[4].Value);
        }

        [TestMethod]
        public async Task ParsesLabelsAndTimestamp()
        {
            var result = await ParseAsync("http_requests_total{method=\"post\",code=\"200\"} 1027 1395066363000");

            Assert.AreEqual(0, result.MalformedLines);
            Assert.AreEqual(1, result.Samples.Count);
            Assert.AreEqual("http_requests_total", result.Samples[0].Name);
            Assert.AreEqual("post", result.Samples[0].Labels["method"]);
            Assert.AreEqual("200", result.Samples[0].Labels["code"]);
            Assert.AreEqual(1027d, result.Samples[0].Value);
            Assert.AreEqual(DateTimeOffset.FromUnixTimeMilliseconds(1395066363000), result.Samples[0].Timestamp);
        }

        [TestMethod]
        public async Task ParsesNegativeTimestamp()
        {
            var result = await ParseAsync("m 1 -1000");

            Assert.AreEqual(0, result.MalformedLines);
            Assert.AreEqual(DateTimeOffset.FromUnixTimeMilliseconds(-1000), result.Samples[0].Timestamp);
        }

        [TestMethod]
        public async Task RejectsOutOfRangeTimestampWithoutThrowing()
        {
            var result = await ParseAsync("m 1 999999999999999999");

            Assert.AreEqual(1, result.MalformedLines);
            Assert.AreEqual(0, result.Samples.Count);
        }

        [TestMethod]
        public async Task DecodesLabelValueEscapes()
        {
            var result = await ParseAsync(@"msg{path=""C:\\dir"",text=""say \""hi\""\nbye""} 1");

            Assert.AreEqual(0, result.MalformedLines);
            Assert.AreEqual(1, result.Samples.Count);
            Assert.AreEqual("C:\\dir", result.Samples[0].Labels["path"]);
            Assert.AreEqual("say \"hi\"\nbye", result.Samples[0].Labels["text"]);
        }

        [TestMethod]
        public async Task KeepsUnknownEscapeSequencesLiterally()
        {
            var result = await ParseAsync(@"m{a=""x\ty""} 1");

            Assert.AreEqual(0, result.MalformedLines);
            Assert.AreEqual("x\\ty", result.Samples[0].Labels["a"]);
        }

        [TestMethod]
        public async Task ParsesNonFiniteValues()
        {
            var text = string.Join('\n',
                "a NaN",
                "b +Inf",
                "c -Inf",
                "d Inf",
                "e nan",
                "f -inf");

            var result = await ParseAsync(text);

            Assert.AreEqual(0, result.MalformedLines);
            Assert.AreEqual(6, result.Samples.Count);
            Assert.IsTrue(double.IsNaN(result.Samples[0].Value));
            Assert.IsTrue(double.IsPositiveInfinity(result.Samples[1].Value));
            Assert.IsTrue(double.IsNegativeInfinity(result.Samples[2].Value));
            Assert.IsTrue(double.IsPositiveInfinity(result.Samples[3].Value));
            Assert.IsTrue(double.IsNaN(result.Samples[4].Value));
            Assert.IsTrue(double.IsNegativeInfinity(result.Samples[5].Value));
        }

        [TestMethod]
        public async Task SkipsCommentsBlankLinesAndEofMarker()
        {
            var text = string.Join('\n',
                "# HELP http_requests_total The total number of HTTP requests.",
                "# TYPE http_requests_total counter",
                "",
                "http_requests_total{method=\"post\",code=\"200\"} 1027",
                "   ",
                "# some other comment",
                "http_requests_total{method=\"post\",code=\"400\"} 3",
                "# EOF");

            var result = await ParseAsync(text);

            Assert.AreEqual(0, result.MalformedLines);
            Assert.AreEqual(2, result.Samples.Count);
            Assert.AreEqual(1027d, result.Samples[0].Value);
            Assert.AreEqual(3d, result.Samples[1].Value);
        }

        [TestMethod]
        public async Task CountsMalformedLinesAndKeepsParsing()
        {
            var text = string.Join('\n',
                "no_value_here",
                "bad{unclosed=\"x\" 1",
                "metric 12 notatimestamp",
                "good_metric 42");

            var result = await ParseAsync(text);

            Assert.AreEqual(3, result.MalformedLines);
            Assert.AreEqual(1, result.Samples.Count);
            Assert.AreEqual("good_metric", result.Samples[0].Name);
            Assert.AreEqual(42d, result.Samples[0].Value);
        }

        [TestMethod]
        public async Task CountsMalformedLabelSyntaxVariants()
        {
            var text = string.Join('\n',
                "m{a=} 1",
                "m{a=\"x} 1",
                "m{=\"x\"} 1",
                "m{a \"x\"} 1",
                "m{a=\"x\"",
                "m 1 2 3",
                "{code=\"200\"} 1");

            var result = await ParseAsync(text);

            Assert.AreEqual(7, result.MalformedLines);
            Assert.AreEqual(0, result.Samples.Count);
        }

        [TestMethod]
        public async Task ParsesHistogramAndSummarySamples()
        {
            var text = string.Join('\n',
                "http_req_duration_bucket{le=\"0.5\"} 129",
                "http_req_duration_bucket{le=\"+Inf\"} 144",
                "http_req_duration_sum 53.4",
                "http_req_duration_count 144",
                "rpc_duration_seconds{quantile=\"0.99\"} 76656");

            var result = await ParseAsync(text);

            Assert.AreEqual(0, result.MalformedLines);
            Assert.AreEqual(5, result.Samples.Count);
            Assert.AreEqual("0.5", result.Samples[0].Labels["le"]);
            Assert.AreEqual("+Inf", result.Samples[1].Labels["le"]);
            Assert.AreEqual(53.4, result.Samples[2].Value);
            Assert.AreEqual(144d, result.Samples[3].Value);
            Assert.AreEqual("0.99", result.Samples[4].Labels["quantile"]);
        }

        [TestMethod]
        public async Task ParsesEmptyBracesAndTrailingComma()
        {
            var text = string.Join('\n',
                "up{} 1",
                "up{job=\"x\",} 1");

            var result = await ParseAsync(text);

            Assert.AreEqual(0, result.MalformedLines);
            Assert.AreEqual(2, result.Samples.Count);
            Assert.AreEqual(0, result.Samples[0].Labels.Count);
            Assert.AreEqual("x", result.Samples[1].Labels["job"]);
        }

        [TestMethod]
        public async Task ParsesQuotedSpecialCharactersInLabelValue()
        {
            var result = await ParseAsync("m{a=\"x,y}z\",b=\"has space\",c=\"{\"} 1");

            Assert.AreEqual(0, result.MalformedLines);
            Assert.AreEqual(1, result.Samples.Count);
            Assert.AreEqual("x,y}z", result.Samples[0].Labels["a"]);
            Assert.AreEqual("has space", result.Samples[0].Labels["b"]);
            Assert.AreEqual("{", result.Samples[0].Labels["c"]);
        }

        [TestMethod]
        public async Task ToleratesExtraWhitespace()
        {
            var text = string.Join('\n',
                "metric_name\t\t42.5   1395066363000",
                "  padded 1  ",
                "spaced {a = \"b\" , c = \"d\"} 2",
                "m{ } 3");

            var result = await ParseAsync(text);

            Assert.AreEqual(0, result.MalformedLines);
            Assert.AreEqual(4, result.Samples.Count);
            Assert.AreEqual(42.5, result.Samples[0].Value);
            Assert.AreEqual(DateTimeOffset.FromUnixTimeMilliseconds(1395066363000), result.Samples[0].Timestamp);
            Assert.AreEqual("padded", result.Samples[1].Name);
            Assert.AreEqual("b", result.Samples[2].Labels["a"]);
            Assert.AreEqual("d", result.Samples[2].Labels["c"]);
            Assert.AreEqual(0, result.Samples[3].Labels.Count);
        }

        [TestMethod]
        public async Task HandlesCrLfLineEndings()
        {
            var result = await ParseAsync("a 1\r\nb 2\r\n");

            Assert.AreEqual(0, result.MalformedLines);
            Assert.AreEqual(2, result.Samples.Count);
            Assert.AreEqual("a", result.Samples[0].Name);
            Assert.AreEqual("b", result.Samples[1].Name);
        }

        [TestMethod]
        public async Task ParsesUtf8QuotedMetricName()
        {
            var result = await ParseAsync("{\"my.utf8.metric\"} 1");

            Assert.AreEqual(0, result.MalformedLines);
            Assert.AreEqual(1, result.Samples.Count);
            Assert.AreEqual("my.utf8.metric", result.Samples[0].Name);
            Assert.AreEqual(0, result.Samples[0].Labels.Count);
            Assert.AreEqual(1d, result.Samples[0].Value);
        }

        [TestMethod]
        public async Task ParsesUtf8QuotedMetricNameWithLabels()
        {
            var result = await ParseAsync("{\"my.utf8.metric\",code=\"200\",\"label.with.dots\"=\"val\"} 5 1395066363000");

            Assert.AreEqual(0, result.MalformedLines);
            Assert.AreEqual(1, result.Samples.Count);
            Assert.AreEqual("my.utf8.metric", result.Samples[0].Name);
            Assert.AreEqual("200", result.Samples[0].Labels["code"]);
            Assert.AreEqual("val", result.Samples[0].Labels["label.with.dots"]);
            Assert.AreEqual(5d, result.Samples[0].Value);
            Assert.AreEqual(DateTimeOffset.FromUnixTimeMilliseconds(1395066363000), result.Samples[0].Timestamp);
        }

        [TestMethod]
        public async Task DecodesEscapesInQuotedMetricName()
        {
            var result = await ParseAsync(@"{""a\""b\\c\nd""} 1");

            Assert.AreEqual(0, result.MalformedLines);
            Assert.AreEqual("a\"b\\c\nd", result.Samples[0].Name);
        }

        [TestMethod]
        public async Task RejectsSecondQuotedMetricName()
        {
            var result = await ParseAsync("{\"a\",\"b\"} 1");

            Assert.AreEqual(1, result.MalformedLines);
            Assert.AreEqual(0, result.Samples.Count);
        }

        [TestMethod]
        public async Task RejectsQuotedMetricNameAfterFirstPosition()
        {
            var result = await ParseAsync("{code=\"200\",\"name\"} 1");

            Assert.AreEqual(1, result.MalformedLines);
            Assert.AreEqual(0, result.Samples.Count);
        }

        [TestMethod]
        public async Task DuplicateLabelNamesLastWins()
        {
            var result = await ParseAsync("m{a=\"1\",a=\"2\"} 3");

            Assert.AreEqual(0, result.MalformedLines);
            Assert.AreEqual("2", result.Samples[0].Labels["a"]);
        }

        [TestMethod]
        public async Task IgnoresOpenMetricsExemplars()
        {
            var text = string.Join('\n',
                "foo_bucket{le=\"0.01\"} 0 1520879607789 # {trace_id=\"abc\"} 0.67",
                "bar 1 # {span_id=\"def\"} 9.8");

            var result = await ParseAsync(text);

            Assert.AreEqual(0, result.MalformedLines);
            Assert.AreEqual(2, result.Samples.Count);
            Assert.AreEqual(0d, result.Samples[0].Value);
            Assert.AreEqual(DateTimeOffset.FromUnixTimeMilliseconds(1520879607789), result.Samples[0].Timestamp);
            Assert.AreEqual(1d, result.Samples[1].Value);
            Assert.IsNull(result.Samples[1].Timestamp);
        }

        [TestMethod]
        public async Task EmptyInputReturnsNoSamples()
        {
            var empty = await ParseAsync(string.Empty);
            var whitespace = await ParseAsync("   \n\t\n");

            Assert.AreEqual(0, empty.MalformedLines);
            Assert.AreEqual(0, empty.Samples.Count);
            Assert.AreEqual(0, whitespace.MalformedLines);
            Assert.AreEqual(0, whitespace.Samples.Count);
        }

        [TestMethod]
        public async Task PreservesSampleOrderAcrossManyLines()
        {
            var builder = new StringBuilder();
            for (int i = 0; i < 10_000; i++)
            {
                builder.Append("metric_").Append(i).Append("{index=\"").Append(i).Append("\"} ").Append(i).Append('\n');
            }

            var result = await ParseAsync(builder.ToString());

            Assert.AreEqual(0, result.MalformedLines);
            Assert.AreEqual(10_000, result.Samples.Count);
            Assert.AreEqual("metric_0", result.Samples[0].Name);
            Assert.AreEqual("metric_9999", result.Samples[9999].Name);
            Assert.AreEqual(9999d, result.Samples[9999].Value);
        }

        [TestMethod]
        public async Task ParsesRealisticExporterPayload()
        {
            var text = string.Join('\n',
                "# HELP go_gc_duration_seconds A summary of the pause duration of garbage collection cycles.",
                "# TYPE go_gc_duration_seconds summary",
                "go_gc_duration_seconds{quantile=\"0\"} 2.7333e-05",
                "go_gc_duration_seconds{quantile=\"1\"} 0.000253637",
                "go_gc_duration_seconds_sum 0.181569925",
                "go_gc_duration_seconds_count 2447",
                "# HELP node_cpu_seconds_total Seconds the CPUs spent in each mode.",
                "# TYPE node_cpu_seconds_total counter",
                "node_cpu_seconds_total{cpu=\"0\",mode=\"idle\"} 1.6462495e+06",
                "node_cpu_seconds_total{cpu=\"0\",mode=\"iowait\"} 461.03",
                "# HELP node_filesystem_avail_bytes Filesystem space available.",
                "node_filesystem_avail_bytes{device=\"/dev/sda1\",mountpoint=\"/\"} 1.2e+10");

            var result = await ParseAsync(text);

            Assert.AreEqual(0, result.MalformedLines);
            Assert.AreEqual(7, result.Samples.Count);
            Assert.AreEqual(2447d, result.Samples[3].Value);
            Assert.AreEqual("idle", result.Samples[4].Labels["mode"]);
            Assert.AreEqual("/dev/sda1", result.Samples[6].Labels["device"]);
        }

        [TestMethod]
        public async Task CancellationIsObserved()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("a 1\n"));

            try
            {
                await PrometheusTextParser.ParseAsync(stream, cts.Token);
                Assert.Fail("Expected OperationCanceledException.");
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
