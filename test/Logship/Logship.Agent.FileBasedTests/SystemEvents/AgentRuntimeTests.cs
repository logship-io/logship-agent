using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Logship.Agent.Core.Records;
using Logship.Agent.Core.Services.Sources.Common;
using Logship.Agent.FileBasedTests.LogFiles;
using Logship.Agent.FileBasedTests.Utility;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;

namespace Logship.Agent.FileBasedTests.SystemEvents;

[TestClass]
public sealed class AgentRuntimeTests
{
    [TestMethod]
    [Timeout(10000)]
    public async Task LogsFirstAcceptedBatchEvenAfterFailedAttempts()
    {
        var options = Options.Create(new OutputConfiguration { Endpoint = "https://logship.example" });
        using var backend = new TestAgentBackend();
        var authentication = backend.CreateAuthentication(options);
        using var logs = new LifecycleLog();
        using var output = new LogshipEventOutput(options, backend, authentication, backend, logs);
        DataRecord[] records = [new("Example", DateTimeOffset.UtcNow, new())];
        Assert.IsFalse(await output.SendAsync(records, CancellationToken.None));
        Assert.AreEqual(0, backend.UploadCount);
        await authentication.PerformRefreshAsync(CancellationToken.None);

        Assert.IsTrue(await output.SendAsync([], CancellationToken.None));
        backend.Upload = _ => Task.FromResult(HttpStatusCode.ServiceUnavailable);
        Assert.IsFalse(await output.SendAsync(records, CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        backend.Upload = token =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<HttpStatusCode>(token);
        };
        Assert.IsFalse(await output.SendAsync(records, cancellation.Token));
        Assert.AreEqual(0, logs.FirstBatchMessages);

        backend.Upload = _ => Task.FromResult(HttpStatusCode.Accepted);
        Assert.IsTrue(await output.SendAsync(records, CancellationToken.None));
        Assert.IsTrue(await output.SendAsync(records, CancellationToken.None));
        Assert.AreEqual(1, logs.FirstBatchMessages);
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task ProcessHealthIsEnabledWithoutDetailedDiagnostics()
    {
        using var meter = new Meter("Logship.Agent.Test");
        var counter = meter.CreateCounter<long>("test.default");
        using var source = new ActivitySource("Logship.Agent.Test");
        var buffer = new TestEventBuffer();
        var service = new InternalMetricsService(Options.Create(new SourcesConfiguration()),
            buffer, NullLogger<InternalMetricsService>.Instance);
        await service.StartAsync(CancellationToken.None);
        try
        {
            counter.Add(1);
            using var activity = source.StartActivity("default");
            Assert.IsNull(activity);
            await EventServiceTests.Until(() => !buffer.Records.IsEmpty);
            Assert.AreEqual("logship.agent.process", buffer.Records.Single().Schema);
            Assert.AreEqual(TimeSpan.FromMinutes(1), new InternalMetricsConfiguration().Interval);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task DetailedDiagnosticsHaveBoundedPendingQueues()
    {
        using var meter = new Meter("Logship.Agent.Test");
        var counter = meter.CreateCounter<long>("test.bounded");
        using var source = new ActivitySource("Logship.Agent.Test");
        var buffer = new TestEventBuffer();
        var service = new InternalMetricsService(Options.Create(new SourcesConfiguration
        {
            Internals = new() { EnableMetrics = true, EnableTracing = true, EnableProcessMetrics = false },
        }), buffer, NullLogger<InternalMetricsService>.Instance);

        for (var i = 0; i < 250; i++)
        {
            counter.Add(1);
            using var activity = source.StartActivity("bounded");
        }

        await service.StartAsync(CancellationToken.None);
        try
        {
            await EventServiceTests.Until(() => buffer.Records.Count >= 200);
            Assert.AreEqual(100, buffer.Records.Count(record => record.Schema == "logship.agent.metrics"));
            Assert.AreEqual(100, buffer.Records.Count(record => record.Schema == "logship.agent.traces"));
            Assert.IsFalse(buffer.Records.Any(record => record.Schema == "logship.agent.process"));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private sealed class LifecycleLog : ILoggerFactory, ILogger
    {
        public int FirstBatchMessages { get; private set; }
        public ILogger CreateLogger(string categoryName) => categoryName == "Logship.Agent.Lifecycle" ? this : NullLogger.Instance;
        public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Information && formatter(state, exception).Contains("pushed first batch", StringComparison.Ordinal))
            {
                FirstBatchMessages++;
            }
        }
        public void Dispose() { }
    }
}
