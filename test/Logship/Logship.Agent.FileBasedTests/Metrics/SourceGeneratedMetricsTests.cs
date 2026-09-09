// <copyright file="SourceGeneratedMetricsTests.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Events;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics.Metrics;

namespace Logship.Agent.FileBasedTests.Metrics
{
    [TestClass]
    public sealed class SourceGeneratedMetricsTests
    {
        [TestMethod]
        public void GeneratedCountersKeepInstrumentNamesAndRecordValues()
        {
            using var meter = new Meter("Logship.Agent.Test.Counters");
            var measurements = new List<(string Instrument, string? Unit, long Value)>();
            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter == meter)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) => measurements.Add((instrument.Name, instrument.Unit, value)));
            listener.Start();

            BufferMetrics.CreateRecordsAdded(meter).Add(3);
            BufferMetrics.CreateRecordsDropped(meter).Add(1);
            BufferMetrics.CreateRecordsFlushed(meter).Add(2);
            SinkMetrics.CreateFlushCount(meter).Add(1);
            SinkMetrics.CreateFlushSuccess(meter).Add(1);
            SinkMetrics.CreateFlushFailure(meter).Add(1);

            CollectionAssert.AreEquivalent(new[]
            {
                ("logship.agent.buffer.records_added", (string?)"records", 3L),
                ("logship.agent.buffer.records_dropped", (string?)"records", 1L),
                ("logship.agent.buffer.records_flushed", (string?)"records", 2L),
                ("logship.agent.sink.flush_count", (string?)"flushes", 1L),
                ("logship.agent.sink.flush_success", (string?)"flushes", 1L),
                ("logship.agent.sink.flush_failure", (string?)"flushes", 1L),
            }, measurements);
        }

        [TestMethod]
        public void GeneratedHistogramKeepsInstrumentNameAndRecordsValue()
        {
            using var meter = new Meter("Logship.Agent.Test.Histogram");
            var measurements = new List<(string Instrument, string? Unit, double Value)>();
            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter == meter)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) => measurements.Add((instrument.Name, instrument.Unit, value)));
            listener.Start();

            SinkMetrics.CreateFlushDuration(meter).Record(12.5);

            Assert.AreEqual(1, measurements.Count);
            Assert.AreEqual("logship.agent.sink.flush_duration", measurements[0].Instrument);
            Assert.AreEqual("ms", measurements[0].Unit);
            Assert.AreEqual(12.5, measurements[0].Value);
        }
    }
}
