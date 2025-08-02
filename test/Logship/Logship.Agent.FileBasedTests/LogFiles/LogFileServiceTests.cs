// <copyright file="LogFileServiceTests.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Logship.Agent.Core.Records;
using Logship.Agent.Core.Services.Sources.Common.LogFile;
using Logship.Agent.FileBasedTests.Utility;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Concurrent;
using System.Text;

namespace Logship.Agent.FileBasedTests.LogFiles
{
    [TestClass]
    public sealed class LogFileServiceTests
    {
        [TestMethod]
        public async Task ShouldReadSingleLineFromFile()
        {
            // Arrange
            using var tempFile = new TempFile();
            var expectedContent = "2023-01-01 10:00:00 INFO Application started";
            await tempFile.AppendLine(expectedContent, Encoding.UTF8, CancellationToken.None);

            var eventBuffer = new TestEventBuffer();
            var logger = new TestLogger<LogFileService>();

            // Act
            using var service = CreateLogFileService(tempFile.FileInfo.FullName, eventBuffer, logger, startAtBeginning: true);
            await RunServiceForDuration(service, TimeSpan.FromSeconds(2));

            // Assert
            Assert.AreEqual(1, eventBuffer.Records.Count);
            var record = eventBuffer.Records.First();
            Assert.AreEqual("LogFile", record.Schema);
            Assert.AreEqual(expectedContent, record.Data["Content"]);
            Assert.AreEqual(tempFile.FileInfo.FullName, record.Data["FilePath"]);
        }

        [TestMethod]
        public async Task ShouldReadMultipleLines()
        {
            // Arrange
            using var tempFile = new TempFile();
            var lines = new[]
            {
                "Line 1",
                "Line 2",
                "Line 3"
            };

            foreach (var line in lines)
            {
                await tempFile.AppendLine(line, Encoding.UTF8, CancellationToken.None);
            }

            var eventBuffer = new TestEventBuffer();
            var logger = new TestLogger<LogFileService>();

            // Act
            using var service = CreateLogFileService(tempFile.FileInfo.FullName, eventBuffer, logger, startAtBeginning: true);
            await RunServiceForDuration(service, TimeSpan.FromSeconds(2));

            // Assert
            Assert.AreEqual(3, eventBuffer.Records.Count);
            var recordContents = eventBuffer.Records.Select(r => r.Data["Content"].ToString()).ToArray();
            CollectionAssert.AreEquivalent(lines, recordContents);
        }

        [TestMethod]
        public async Task ShouldHandleFileAppend()
        {
            // Arrange
            using var tempFile = new TempFile();
            await tempFile.AppendLine("Initial line", Encoding.UTF8, CancellationToken.None);

            var eventBuffer = new TestEventBuffer();
            var logger = new TestLogger<LogFileService>();

            using var service = CreateLogFileService(tempFile.FileInfo.FullName, eventBuffer, logger, startAtBeginning: true);
            await RunServiceForDuration(service, TimeSpan.FromSeconds(1));

            var initialCount = eventBuffer.Records.Count;

            // Act - append more content
            await tempFile.AppendLine("Appended line", Encoding.UTF8, CancellationToken.None);
            await RunServiceForDuration(service, TimeSpan.FromSeconds(2));

            Assert.IsTrue(eventBuffer.Records.Count > initialCount, $"Should capture appended content. Initial: {initialCount}, Final: {eventBuffer.Records.Count}");

            // Find the appended line record
            var appendedRecord = eventBuffer.Records.FirstOrDefault(r =>
                r.Data["Content"].ToString() == "Appended line");
            Assert.IsNotNull(appendedRecord, "Should find the appended line record");
        }

        [TestMethod]
        public async Task ShouldHandleFileRotation()
        {
            // Arrange
            using var tempFile = new TempFile();
            await tempFile.AppendLine("Original content", Encoding.UTF8, CancellationToken.None);

            var eventBuffer = new TestEventBuffer();
            var logger = new TestLogger<LogFileService>();

            using var service = CreateLogFileService(tempFile.FileInfo.FullName, eventBuffer, logger, startAtBeginning: true);
            await RunServiceForDuration(service, TimeSpan.FromSeconds(1));

            var initialCount = eventBuffer.Records.Count;

            // Act - simulate file rotation by creating new file with same name
            tempFile.FileInfo.Delete();
            await tempFile.AppendLine("New file content after rotation", Encoding.UTF8, CancellationToken.None);
            await RunServiceForDuration(service, TimeSpan.FromSeconds(2));

            // Assert
            Assert.IsTrue(eventBuffer.Records.Count > initialCount, "Should capture content from rotated file");
            var rotatedRecord = eventBuffer.Records.FirstOrDefault(r =>
                r.Data["Content"].ToString() == "New file content after rotation");
            Assert.IsNotNull(rotatedRecord, "Should find record from rotated file");
        }

        [TestMethod]
        public async Task ShouldHandleMultipleFileChanges()
        {
            // Arrange
            using var tempFile = new TempFile();
            await tempFile.AppendLine("Line 1", Encoding.UTF8, CancellationToken.None);

            var eventBuffer = new TestEventBuffer();
            var logger = new TestLogger<LogFileService>();

            using var service = CreateLogFileService(tempFile.FileInfo.FullName, eventBuffer, logger, startAtBeginning: true);
            await RunServiceForDuration(service, TimeSpan.FromSeconds(1));

            // Act - make multiple rapid changes
            await tempFile.AppendLine("Line 2", Encoding.UTF8, CancellationToken.None);
            await Task.Delay(100);
            await tempFile.AppendLine("Line 3", Encoding.UTF8, CancellationToken.None);
            await Task.Delay(100);
            await tempFile.AppendLine("Line 4", Encoding.UTF8, CancellationToken.None);
            
            await RunServiceForDuration(service, TimeSpan.FromSeconds(3));

            // Assert
            Assert.IsTrue(eventBuffer.Records.Count >= 4, $"Should capture at least all lines from multiple changes. Got: {eventBuffer.Records.Count}");
            var contents = eventBuffer.Records.Select(r => r.Data["Content"].ToString()).ToArray();
            CollectionAssert.Contains(contents, "Line 1");
            CollectionAssert.Contains(contents, "Line 2");
            CollectionAssert.Contains(contents, "Line 3");
            CollectionAssert.Contains(contents, "Line 4");
        }

        [TestMethod]
        public async Task ShouldHandleFileTruncation()
        {
            // Arrange
            using var tempFile = new TempFile();
            await tempFile.AppendLine("Line 1", Encoding.UTF8, CancellationToken.None);
            await tempFile.AppendLine("Line 2", Encoding.UTF8, CancellationToken.None);

            var eventBuffer = new TestEventBuffer();
            var logger = new TestLogger<LogFileService>();

            using var service = CreateLogFileService(tempFile.FileInfo.FullName, eventBuffer, logger, startAtBeginning: true);
            await RunServiceForDuration(service, TimeSpan.FromSeconds(1));

            var initialCount = eventBuffer.Records.Count;

            // Act - truncate file and add new content
            await tempFile.Truncate();
            await tempFile.AppendLine("New line after truncation", Encoding.UTF8, CancellationToken.None);
            await RunServiceForDuration(service, TimeSpan.FromSeconds(2));

            // Assert
            Assert.IsTrue(eventBuffer.Records.Count > initialCount, "Should capture content after truncation");
            var truncatedRecord = eventBuffer.Records.FirstOrDefault(r =>
                r.Data["Content"].ToString() == "New line after truncation");
            Assert.IsNotNull(truncatedRecord, "Should find record after file truncation");
        }

        [TestMethod]
        public async Task ShouldReadMultilineJavaStackTrace()
        {
            // Arrange
            using var tempFile = new TempFile();
            await tempFile.AppendLine("2023-01-01 10:00:00 ERROR Exception occurred", Encoding.UTF8, CancellationToken.None);
            await tempFile.AppendLine("    at com.example.Service.method(Service.java:123)", Encoding.UTF8, CancellationToken.None);
            await tempFile.AppendLine("    at com.example.Controller.handle(Controller.java:456)", Encoding.UTF8, CancellationToken.None);
            await tempFile.AppendLine("    Caused by: java.lang.RuntimeException: Test error", Encoding.UTF8, CancellationToken.None);
            await tempFile.AppendLine("2023-01-01 10:00:01 INFO Next log entry", Encoding.UTF8, CancellationToken.None);

            var eventBuffer = new TestEventBuffer();
            var logger = new TestLogger<LogFileService>();

            // Act
            using var service = CreateLogFileServiceWithMultiline(tempFile.FileInfo.FullName, @"^\d{4}-\d{2}-\d{2}", eventBuffer, logger, startAtBeginning: true);
            await RunServiceForDuration(service, TimeSpan.FromSeconds(3));

            // Assert
            Assert.IsTrue(eventBuffer.Records.Count >= 2, $"Should have at least 2 multiline entries, but got {eventBuffer.Records.Count}");

            // Find the multiline record containing the stack trace
            var stackTraceRecord = eventBuffer.Records.FirstOrDefault(r =>
                r.Data["Content"].ToString()!.Contains("Exception occurred"));

            Assert.IsNotNull(stackTraceRecord, "Should find record containing 'Exception occurred'");
            Assert.IsTrue(stackTraceRecord.Data["Content"].ToString()!.Contains("Caused by"),
                "Stack trace record should contain 'Caused by'");
            Assert.IsNotNull(stackTraceRecord.Data["MultilineId"],
                "Stack trace record should have MultilineId");
        }

        [TestMethod]
        public async Task ShouldMatchGlobPatterns()
        {
            // Arrange
            using var tempDir = new TempDirectory();
            using var logFile = new TempFile(Path.Combine(tempDir.Path, "app.log"));
            using var txtFile = new TempFile(Path.Combine(tempDir.Path, "data.txt"));
            using var otherFile = new TempFile(Path.Combine(tempDir.Path, "config.json"));

            await logFile.AppendLine("Log entry", Encoding.UTF8, CancellationToken.None);
            await txtFile.AppendLine("Text entry", Encoding.UTF8, CancellationToken.None);
            await otherFile.AppendLine("JSON entry", Encoding.UTF8, CancellationToken.None);

            var eventBuffer = new TestEventBuffer();
            var logger = new TestLogger<LogFileService>();

            // Act
            var patterns = new[] { "*.log", "*.txt" };
            using var service = CreateLogFileServiceWithPatterns(patterns, tempDir.Path, eventBuffer, logger, startAtBeginning: true);
            await RunServiceForDuration(service, TimeSpan.FromSeconds(3));

            // Assert
            Assert.AreEqual(2, eventBuffer.Records.Count, "Should only capture .log and .txt files");
            var filePaths = eventBuffer.Records.Select(r => Path.GetFileName(r.Data["FilePath"].ToString())).ToArray();
            CollectionAssert.Contains(filePaths, "app.log");
            CollectionAssert.Contains(filePaths, "data.txt");
        }

        [TestMethod]
        public async Task ShouldExcludeFilesByPattern()
        {
            // Arrange
            using var tempDir = new TempDirectory();
            using var logFile = new TempFile(Path.Combine(tempDir.Path, "app.log"));
            using var debugFile = new TempFile(Path.Combine(tempDir.Path, "debug.log"));

            await logFile.AppendLine("App log entry", Encoding.UTF8, CancellationToken.None);
            await debugFile.AppendLine("Debug log entry", Encoding.UTF8, CancellationToken.None);

            var eventBuffer = new TestEventBuffer();
            var logger = new TestLogger<LogFileService>();

            // Act
            var includePatterns = new[] { "*.log" };
            var excludePatterns = new[] { "debug.log" };
            using var service = CreateLogFileServiceWithPatternsAndExcludes(includePatterns, excludePatterns, tempDir.Path, eventBuffer, logger, startAtBeginning: true);
            await RunServiceForDuration(service, TimeSpan.FromSeconds(3));

            // Assert
            Assert.AreEqual(1, eventBuffer.Records.Count, "Should exclude debug.log");
            var record = eventBuffer.Records.First();
            Assert.AreEqual("App log entry", record.Data["Content"]);
        }

        [TestMethod]
        public async Task ShouldHandleUtf8Encoding()
        {
            // Arrange
            using var tempFile = new TempFile();
            var content = "Üñíçødé tëxt with UTF-8 characters: 中文 العربية";
            await tempFile.Append(content + Environment.NewLine, Encoding.UTF8, CancellationToken.None);

            var eventBuffer = new TestEventBuffer();
            var logger = new TestLogger<LogFileService>();

            // Act
            using var service = CreateLogFileServiceWithEncoding(tempFile.FileInfo.FullName, "utf-8", eventBuffer, logger, startAtBeginning: true);
            await RunServiceForDuration(service, TimeSpan.FromSeconds(2));

            // Assert
            Assert.AreEqual(1, eventBuffer.Records.Count);
            var record = eventBuffer.Records.First();
            Assert.AreEqual(content, record.Data["Content"]);
        }

        [TestMethod]
        public async Task ShouldHandleUnicodeCharacters()
        {
            // Arrange
            using var tempFile = new TempFile();
            var content = "Unicode characters: こんにちは Привет العربية";
            await tempFile.Append(content + Environment.NewLine, Encoding.UTF8, CancellationToken.None);

            var eventBuffer = new TestEventBuffer();
            var logger = new TestLogger<LogFileService>();

            // Act - Test Unicode content with UTF-8 encoding
            using var service = CreateLogFileServiceWithEncoding(tempFile.FileInfo.FullName, "utf-8", eventBuffer, logger, startAtBeginning: true);
            await RunServiceForDuration(service, TimeSpan.FromSeconds(2));

            // Assert
            Assert.AreEqual(1, eventBuffer.Records.Count);
            var record = eventBuffer.Records.First();
            Assert.AreEqual(content, record.Data["Content"]);
        }

        [TestMethod]
        public async Task ShouldHandleAsciiEncoding()
        {
            // Arrange
            using var tempFile = new TempFile();
            var content = "ASCII only content 123 ABC";
            await tempFile.Append(content + Environment.NewLine, Encoding.ASCII, CancellationToken.None);

            var eventBuffer = new TestEventBuffer();
            var logger = new TestLogger<LogFileService>();

            // Act
            using var service = CreateLogFileServiceWithEncoding(tempFile.FileInfo.FullName, "ascii", eventBuffer, logger, startAtBeginning: true);
            await RunServiceForDuration(service, TimeSpan.FromSeconds(2));

            // Assert
            Assert.AreEqual(1, eventBuffer.Records.Count);
            var record = eventBuffer.Records.First();
            Assert.AreEqual(content, record.Data["Content"]);
        }

        [TestMethod]
        public async Task ShouldFallbackToUtf8OnInvalidEncoding()
        {
            // Arrange
            using var tempFile = new TempFile();
            var content = "Test content with fallback encoding";
            await tempFile.Append(content + Environment.NewLine, Encoding.UTF8, CancellationToken.None);

            var eventBuffer = new TestEventBuffer();
            var logger = new TestLogger<LogFileService>();

            // Act
            using var service = CreateLogFileServiceWithEncoding(tempFile.FileInfo.FullName, "invalid-encoding", eventBuffer, logger, startAtBeginning: true);
            await RunServiceForDuration(service, TimeSpan.FromSeconds(2));

            // Assert
            Assert.AreEqual(1, eventBuffer.Records.Count);
            var record = eventBuffer.Records.First();
            Assert.AreEqual(content, record.Data["Content"]);
        }

        [TestMethod]
        public async Task ShouldTruncateLongLines()
        {
            // Arrange
            using var tempFile = new TempFile();
            var longLine = new string('A', 2_000_000); // 2MB line
            await tempFile.AppendLine(longLine, Encoding.UTF8, CancellationToken.None);

            var eventBuffer = new TestEventBuffer();
            var logger = new TestLogger<LogFileService>();

            // Act
            using var service = CreateLogFileServiceWithMaxLineBytes(tempFile.FileInfo.FullName, 1024, eventBuffer, logger, startAtBeginning: true);
            await RunServiceForDuration(service, TimeSpan.FromSeconds(2));

            // Assert
            Assert.AreEqual(1, eventBuffer.Records.Count);
            var record = eventBuffer.Records.First();
            var content = record.Data["Content"].ToString()!;
            Assert.AreEqual(1024, content.Length, "Line should be truncated to max length");
        }

        [TestMethod]
        public async Task ShouldIncludeFileMetadata()
        {
            // Arrange
            using var tempFile = new TempFile();
            await tempFile.AppendLine("Test line", Encoding.UTF8, CancellationToken.None);

            var eventBuffer = new TestEventBuffer();
            var logger = new TestLogger<LogFileService>();

            // Act
            using var service = CreateLogFileService(tempFile.FileInfo.FullName, eventBuffer, logger, startAtBeginning: true);
            await RunServiceForDuration(service, TimeSpan.FromSeconds(2));

            // Assert
            Assert.AreEqual(1, eventBuffer.Records.Count);
            var record = eventBuffer.Records.First();

            Assert.AreEqual(tempFile.FileInfo.FullName, record.Data["FilePath"]);
            Assert.AreEqual("Test line", record.Data["Content"]);
            Assert.IsTrue(record.Data.ContainsKey("LineNumber"));
            Assert.IsTrue(record.Data.ContainsKey("ByteOffset"));
            Assert.IsTrue(record.Data.ContainsKey("FileSize"));
            Assert.IsTrue(record.Data.ContainsKey("ModifiedTime"));
        }

        private static LogFileService CreateLogFileService(string filePath, TestEventBuffer eventBuffer, TestLogger<LogFileService> logger, bool startAtBeginning = false)
        {
            var workingDirectory = Path.GetDirectoryName(filePath)!;
            var relativePath = Path.GetFileName(filePath);

            var config = new LogFileServiceConfiguration
            {
                Enabled = true,
                Include = new[] { relativePath },
                WorkingDirectory = workingDirectory,
                GlobMinimumCooldownMs = 100,
                StartAtBeginning = startAtBeginning
            };

            var sourcesConfig = new SourcesConfiguration { LogFile = config };
            var options = Options.Create(sourcesConfig);
            var outputOptions = Options.Create(new OutputConfiguration() { DataPath = Path.Combine(Path.GetTempPath(), $"test-temp-{Guid.NewGuid()}") });

            return new LogFileService(options, outputOptions, eventBuffer, logger);
        }

        private static LogFileService CreateLogFileServiceWithMultiline(string filePath, string startPattern, TestEventBuffer eventBuffer, TestLogger<LogFileService> logger, bool startAtBeginning = false)
        {
            var workingDirectory = Path.GetDirectoryName(filePath)!;
            var relativePath = Path.GetFileName(filePath);

            var config = new LogFileServiceConfiguration
            {
                Enabled = true,
                Include = new[] { relativePath },
                WorkingDirectory = workingDirectory,
                GlobMinimumCooldownMs = 100,
                StartAtBeginning = startAtBeginning,
                Multiline = new MultilineConfiguration
                {
                    StartPattern = startPattern,
                    Mode = "start_pattern",
                    TimeoutMs = 500
                }
            };

            var sourcesConfig = new SourcesConfiguration { LogFile = config };
            var options = Options.Create(sourcesConfig);
            var outputOptions = Options.Create(new OutputConfiguration() { DataPath = Path.Combine(Path.GetTempPath(), $"test-temp-{Guid.NewGuid()}") });

            return new LogFileService(options, outputOptions, eventBuffer, logger);
        }

        private static LogFileService CreateLogFileServiceWithPatterns(string[] patterns, string workingDirectory, TestEventBuffer eventBuffer, TestLogger<LogFileService> logger, bool startAtBeginning = false)
        {
            var config = new LogFileServiceConfiguration
            {
                Enabled = true,
                Include = patterns,
                WorkingDirectory = workingDirectory,
                GlobMinimumCooldownMs = 100,
                StartAtBeginning = startAtBeginning
            };

            var sourcesConfig = new SourcesConfiguration { LogFile = config };
            var options = Options.Create(sourcesConfig);
            var outputOptions = Options.Create(new OutputConfiguration() { DataPath = Path.Combine(Path.GetTempPath(), $"test-temp-{Guid.NewGuid()}") });

            return new LogFileService(options, outputOptions, eventBuffer, logger);
        }

        private static LogFileService CreateLogFileServiceWithPatternsAndExcludes(string[] includePatterns, string[] excludePatterns, string workingDirectory, TestEventBuffer eventBuffer, TestLogger<LogFileService> logger, bool startAtBeginning = false)
        {
            var config = new LogFileServiceConfiguration
            {
                Enabled = true,
                Include = includePatterns,
                Exclude = excludePatterns,
                WorkingDirectory = workingDirectory,
                GlobMinimumCooldownMs = 100,
                StartAtBeginning = startAtBeginning
            };

            var sourcesConfig = new SourcesConfiguration { LogFile = config };
            var options = Options.Create(sourcesConfig);
            var outputOptions = Options.Create(new OutputConfiguration() { DataPath = Path.Combine(Path.GetTempPath(), $"test-temp-{Guid.NewGuid()}") });

            return new LogFileService(options, outputOptions, eventBuffer, logger);
        }

        private static LogFileService CreateLogFileServiceWithEncoding(string filePath, string encoding, TestEventBuffer eventBuffer, TestLogger<LogFileService> logger, bool startAtBeginning = false)
        {
            var workingDirectory = Path.GetDirectoryName(filePath)!;
            var relativePath = Path.GetFileName(filePath);

            var config = new LogFileServiceConfiguration
            {
                Enabled = true,
                Include = new[] { relativePath },
                WorkingDirectory = workingDirectory,
                Encoding = encoding,
                GlobMinimumCooldownMs = 100,
                StartAtBeginning = startAtBeginning
            };

            var sourcesConfig = new SourcesConfiguration { LogFile = config };
            var options = Options.Create(sourcesConfig);
            var outputOptions = Options.Create(new OutputConfiguration() { DataPath = Path.Combine(Path.GetTempPath(), $"test-temp-{Guid.NewGuid()}") });

            return new LogFileService(options, outputOptions, eventBuffer, logger);
        }

        private static LogFileService CreateLogFileServiceWithMaxLineBytes(string filePath, int maxLineBytes, TestEventBuffer eventBuffer, TestLogger<LogFileService> logger, bool startAtBeginning = false)
        {
            var workingDirectory = Path.GetDirectoryName(filePath)!;
            var relativePath = Path.GetFileName(filePath);

            var config = new LogFileServiceConfiguration
            {
                Enabled = true,
                Include = new[] { relativePath },
                WorkingDirectory = workingDirectory,
                MaxLineBytes = maxLineBytes,
                GlobMinimumCooldownMs = 100,
                StartAtBeginning = startAtBeginning
            };

            var sourcesConfig = new SourcesConfiguration { LogFile = config };
            var options = Options.Create(sourcesConfig);
            var outputOptions = Options.Create(new OutputConfiguration() { DataPath = Path.Combine(Path.GetTempPath(), $"test-temp-{Guid.NewGuid()}") });

            return new LogFileService(options, outputOptions, eventBuffer, logger);
        }

        private static async Task RunServiceForDuration(LogFileService service, TimeSpan duration)
        {
            using var cts = new CancellationTokenSource(duration);
            await service.StartAsync(cts.Token);

            try
            {
                await Task.Delay(duration, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation token expires
            }

            await service.StopAsync(CancellationToken.None);
        }
    }

    internal sealed class TestEventBuffer : IEventBuffer
    {
        public ConcurrentBag<DataRecord> Records { get; } = new ConcurrentBag<DataRecord>();

        public void Add(DataRecord record)
        {
            Records.Add(record);
        }

        public void Add(IReadOnlyCollection<DataRecord> data)
        {
            foreach (var record in data)
            {
                Records.Add(record);
            }
        }

        public Task<IReadOnlyCollection<DataRecord>> NextAsync(CancellationToken token)
        {
            return Task.FromResult<IReadOnlyCollection<DataRecord>>(Records.ToArray());
        }
    }

    internal sealed class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
