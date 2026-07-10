// <copyright file="LinuxDockerService.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Net.Sockets;
using System.Text.Json;

namespace Logship.Agent.Core.Services.Sources.Linux.Security
{
    /// <summary>
    /// Collects Docker daemon and container telemetry useful for Linux security monitoring.
    /// </summary>
    /// <remarks>
    /// Configure with <c>Sources:Linux.Docker</c>. Docker socket collection is disabled unless the source is
    /// enabled and <c>useDockerSocket</c> is true. <c>dockerSocketPath</c> controls the Unix socket path,
    /// <c>collectDockerEvents</c> controls bounded reads from the Docker events API, <c>collectContainerLogs</c>
    /// controls Docker json-file log tailing, and <c>jsonLogRoot</c> controls the json-file root. Socket
    /// access uses read-only Docker HTTP endpoints for daemon info, container inventory, image inventory,
    /// and lifecycle events. The service emits <c>Linux.Docker.Info</c>, <c>Linux.Docker.Container</c>,
    /// <c>Linux.Docker.Image</c>, <c>Linux.Docker.Event</c>, and <c>Linux.Docker.Log</c>. It does not call
    /// mutating Docker APIs, inspect container filesystems, exec into containers, or collect environment
    /// variables/secrets by default.
    /// </remarks>
    internal sealed class LinuxDockerService : BaseIntervalInputService<LinuxDockerConfiguration>, IDisposable
    {
        private readonly Dictionary<string, long> logOffsets = new(StringComparer.Ordinal);
        private readonly HttpClient? dockerClient;
        private long lastEventUnixSeconds;

        public LinuxDockerService(IOptions<SourcesConfiguration> config, IEventBuffer buffer, ILogger<LinuxDockerService> logger)
            : base(config.Value.LinuxDocker, buffer, nameof(LinuxDockerService), logger)
        {
            if (this.Enabled && false == OperatingSystem.IsLinux())
            {
                ServiceLog.SkipPlatformServiceExecution(Logger, nameof(LinuxDockerService), Environment.OSVersion);
                this.Enabled = false;
            }

            if (this.Enabled && this.Config.UseDockerSocket && File.Exists(this.Config.DockerSocketPath))
            {
                this.dockerClient = CreateDockerClient(this.Config.DockerSocketPath);
            }

            this.lastEventUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        protected override bool ExitOnException => false;

        public void Dispose()
        {
            this.dockerClient?.Dispose();
        }

        protected override async Task ExecuteSingleAsync(CancellationToken token)
        {
            if (this.dockerClient != null)
            {
                await this.CollectSocketDataAsync(token);
            }

            if (this.Config.CollectContainerLogs)
            {
                await this.CollectJsonLogsAsync(token);
            }
        }

        private static HttpClient CreateDockerClient(string socketPath)
        {
            var handler = new SocketsHttpHandler
            {
                ConnectCallback = async (_, cancellationToken) =>
                {
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    try
                    {
                        await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }
            };

            return new HttpClient(handler)
            {
                BaseAddress = new Uri("http://docker"),
                Timeout = TimeSpan.FromSeconds(10),
            };
        }

        private async Task CollectSocketDataAsync(CancellationToken token)
        {
            await this.CollectDockerInfoAsync(token);
            await this.CollectContainersAsync(token);
            await this.CollectImagesAsync(token);

            if (this.Config.CollectDockerEvents)
            {
                await this.CollectEventsAsync(token);
            }
        }

        private async Task CollectDockerInfoAsync(CancellationToken token)
        {
            using var doc = await this.GetJsonAsync("/info", token);
            if (doc == null)
            {
                return;
            }

            var record = CreateRecord("Linux.Docker.Info");
            CopyKnownProperties(record.Data, doc.RootElement, [
                "ID",
                "Name",
                "ServerVersion",
                "StorageDriver",
                "CgroupDriver",
                "CgroupVersion",
                "OSType",
                "Architecture",
                "OperatingSystem",
                "KernelVersion",
                "DockerRootDir",
                "SecurityOptions",
                "Swarm",
                "Runtimes",
            ]);
            this.Buffer.Add(record);
        }

        private async Task CollectContainersAsync(CancellationToken token)
        {
            using var doc = await this.GetJsonAsync("/containers/json?all=1", token);
            if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var container in doc.RootElement.EnumerateArray())
            {
                var record = CreateRecord("Linux.Docker.Container");
                CopyKnownProperties(record.Data, container, [
                    "Id",
                    "Names",
                    "Image",
                    "ImageID",
                    "Command",
                    "Created",
                    "Ports",
                    "Labels",
                    "State",
                    "Status",
                    "HostConfig",
                    "NetworkSettings",
                    "Mounts",
                ]);
                this.Buffer.Add(record);
            }
        }

        private async Task CollectImagesAsync(CancellationToken token)
        {
            using var doc = await this.GetJsonAsync("/images/json", token);
            if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var image in doc.RootElement.EnumerateArray())
            {
                var record = CreateRecord("Linux.Docker.Image");
                CopyKnownProperties(record.Data, image, [
                    "Id",
                    "ParentId",
                    "RepoTags",
                    "RepoDigests",
                    "Created",
                    "Size",
                    "VirtualSize",
                    "Labels",
                ]);
                this.Buffer.Add(record);
            }
        }

        private async Task CollectEventsAsync(CancellationToken token)
        {
            var until = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var since = this.lastEventUnixSeconds;
            if (until <= since)
            {
                return;
            }

            this.lastEventUnixSeconds = until;
            using var response = await this.dockerClient!.GetAsync($"/events?since={since.ToString(CultureInfo.InvariantCulture)}&until={until.ToString(CultureInfo.InvariantCulture)}", token);
            if (false == response.IsSuccessStatusCode)
            {
                return;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var reader = new StreamReader(stream);
            while (false == token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(token);
                if (line == null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using var doc = JsonDocument.Parse(line);
                var record = CreateRecord("Linux.Docker.Event");
                CopyKnownProperties(record.Data, doc.RootElement, [
                    "Type",
                    "Action",
                    "Actor",
                    "time",
                    "timeNano",
                    "status",
                    "id",
                    "from",
                ]);
                this.Buffer.Add(record);
            }
        }

        private async Task CollectJsonLogsAsync(CancellationToken token)
        {
            if (false == Directory.Exists(this.Config.JsonLogRoot))
            {
                return;
            }

            foreach (var path in Directory.EnumerateFiles(this.Config.JsonLogRoot, "*-json.log", SearchOption.AllDirectories))
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                await this.ReadJsonLogAsync(path, token);
            }
        }

        private async Task ReadJsonLogAsync(string path, CancellationToken token)
        {
            var fileInfo = new FileInfo(path);
            if (false == this.logOffsets.TryGetValue(path, out var offset))
            {
                offset = fileInfo.Length;
                this.logOffsets[path] = offset;
                return;
            }
            else if (fileInfo.Length < offset)
            {
                offset = 0L;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(offset, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);

            while (false == token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(token);
                if (line == null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var record = CreateRecord("Linux.Docker.Log");
                record.Data["SourcePath"] = path;
                record.Data["ContainerId"] = GetContainerIdFromLogPath(path);
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    CopyKnownProperties(record.Data, doc.RootElement, [
                        "log",
                        "stream",
                        "time",
                    ]);
                }
                catch (JsonException ex)
                {
                    record.Data["RawMessage"] = line;
                    record.Data["ParseError"] = ex.Message;
                }

                this.Buffer.Add(record);
            }

            this.logOffsets[path] = stream.Position;
        }

        private async Task<JsonDocument?> GetJsonAsync(string path, CancellationToken token)
        {
            try
            {
                using var response = await this.dockerClient!.GetAsync(path, token);
                if (false == response.IsSuccessStatusCode)
                {
                    return null;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(token);
                return await JsonDocument.ParseAsync(stream, cancellationToken: token);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or TaskCanceledException)
            {
                LinuxDockerServiceLog.DockerRequestFailed(this.Logger, path, ex);
                return null;
            }
        }

        private static string GetContainerIdFromLogPath(string path)
        {
            var file = Path.GetFileName(path);
            if (file.EndsWith("-json.log", StringComparison.Ordinal))
            {
                return file.Substring(0, file.Length - "-json.log".Length);
            }

            return Path.GetFileName(Path.GetDirectoryName(path)) ?? string.Empty;
        }

        private static void CopyKnownProperties(Dictionary<string, object> data, JsonElement element, IReadOnlyCollection<string> names)
        {
            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out var value))
                {
                    data[name] = ToDataValue(value);
                }
            }
        }

        private static object ToDataValue(JsonElement value)
        {
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number when value.TryGetInt64(out var longValue) => longValue,
                JsonValueKind.Number when value.TryGetDouble(out var doubleValue) => doubleValue,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => string.Empty,
                _ => value.GetRawText(),
            };
        }
    }

    internal static partial class LinuxDockerServiceLog
    {
        [LoggerMessage(LogLevel.Warning, "Docker request failed for {Path}.")]
        public static partial void DockerRequestFailed(ILogger logger, string path, Exception exception);
    }
}
