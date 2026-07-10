// <copyright file="LinuxAuthEventsService.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Logship.Agent.Core.Services.Sources.Linux.Security
{
    /// <summary>
    /// Tails Linux authentication logs and emits normalized security events.
    /// </summary>
    /// <remarks>
    /// Configure with <c>Sources:Linux.AuthEvents</c>. <c>logPaths</c> controls the auth log files to read
    /// and should usually include distro defaults such as <c>/var/log/auth.log</c> and
    /// <c>/var/log/secure</c>. <c>interval</c> controls how often files are polled. When
    /// <c>startAtEnd</c> is true, the service starts from the current end of each file and only emits new
    /// lines; when false, it reads existing content on first startup. The service emits
    /// <c>Linux.Auth.Event</c> records for recognized SSH, sudo, su, and PAM authentication/session lines.
    /// It does not read password hashes or secret material.
    /// </remarks>
    internal sealed class LinuxAuthEventsService : BaseIntervalInputService<LinuxAuthEventsConfiguration>
    {
        private readonly Dictionary<string, long> offsets = new(StringComparer.Ordinal);

        public LinuxAuthEventsService(IOptions<SourcesConfiguration> config, IEventBuffer buffer, ILogger<LinuxAuthEventsService> logger)
            : base(config.Value.LinuxAuthEvents, buffer, nameof(LinuxAuthEventsService), logger)
        {
            if (this.Enabled && false == OperatingSystem.IsLinux())
            {
                ServiceLog.SkipPlatformServiceExecution(Logger, nameof(LinuxAuthEventsService), Environment.OSVersion);
                this.Enabled = false;
            }
        }

        protected override bool ExitOnException => false;

        protected override async Task ExecuteSingleAsync(CancellationToken token)
        {
            foreach (var path in this.Config.LogPaths)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                await this.ReadPathAsync(path, token);
            }
        }

        private async Task ReadPathAsync(string path, CancellationToken token)
        {
            if (false == File.Exists(path))
            {
                return;
            }

            var fileInfo = new FileInfo(path);
            if (false == this.offsets.TryGetValue(path, out var offset))
            {
                offset = this.Config.StartAtEnd ? fileInfo.Length : 0L;
                this.offsets[path] = offset;
                if (this.Config.StartAtEnd)
                {
                    return;
                }
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

                var parsed = LinuxAuthEventParser.TryParse(line, path);
                if (parsed != null)
                {
                    var record = CreateRecord("Linux.Auth.Event");
                    foreach (var item in parsed)
                    {
                        record.Data[item.Key] = item.Value;
                    }

                    this.Buffer.Add(record);
                }
            }

            this.offsets[path] = stream.Position;
        }
    }
}
