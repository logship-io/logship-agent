using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Services.Sources.Common.LogFile;
using Logship.Agent.FileBasedTests.Utility;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text;

namespace Logship.Agent.FileBasedTests.LogFiles;

[TestClass]
public sealed class ConcurrentLogFileTests
{
    [TestMethod]
    [Timeout(10000)]
    public async Task ConcurrentPollsShouldReadLongLineOnlyOnce()
    {
        using var file = new TempFile();
        using var data = new TempDirectory();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await file.AppendLine(new string('A', 2_000_000), Encoding.UTF8, timeout.Token);
        using var logger = new BlockingReadLogger();
        var buffer = new TestEventBuffer();
        using var service = new LogFileService(Options.Create(new SourcesConfiguration
        {
            LogFile = new()
            {
                Enabled = true, StartAtBeginning = true, MaxLineBytes = 1024,
                WorkingDirectory = file.FileInfo.DirectoryName!, Include = [file.FileInfo.Name],
            },
        }), Options.Create(new OutputConfiguration { DataPath = data.Path }), buffer, logger);

        var first = Task.Run(() => service.ProcessFilesAsync(timeout.Token), timeout.Token);
        Task? second = null;
        try
        {
            await logger.FirstRead.Task.WaitAsync(timeout.Token);
            second = service.ProcessFilesAsync(timeout.Token);
            // The first read is paused between reading a chunk and parsing it. An unguarded
            // second poll can consume the remainder of the file in that interval.
            await Task.WhenAny(logger.OverlappingRead.Task, Task.Delay(200, timeout.Token));
        }
        finally
        {
            logger.Release();
            await first;
            if (second != null) await second;
        }

        Assert.AreEqual(1, buffer.Records.Count);
        Assert.IsFalse(logger.OverlappingRead.Task.IsCompleted, "Another poll read the file while its first read was still active.");
        Assert.AreEqual(new string('A', 1024), buffer.Records.Single().Data["Content"]);
        await service.ProcessFilesAsync(timeout.Token);
        Assert.AreEqual(1, buffer.Records.Count, "Polling again must not reread the completed line.");
    }

    private sealed class BlockingReadLogger : ILogger<LogFileService>, IDisposable
    {
        private readonly ManualResetEventSlim release = new(false);
        private int first;
        public TaskCompletionSource FirstRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource OverlappingRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (eventId.Name != "ProcessingBytes") return;
            if (Interlocked.Exchange(ref first, 1) == 0)
            {
                FirstRead.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Test did not release the first file read.");
            }
            else if (!release.IsSet) OverlappingRead.TrySetResult();
        }
        public void Release() => release.Set();
        public void Dispose() => release.Dispose();
    }
}
