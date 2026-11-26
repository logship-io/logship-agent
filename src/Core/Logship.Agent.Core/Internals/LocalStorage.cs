// <copyright file="LocalStorage.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Logship.Agent.Core.Internals
{
    public sealed class LocalStorage : ITokenStorage
    {
        private readonly string TokenFileName = "refresh-token";

        private readonly string DataPath;
        private readonly ILogger<LocalStorage> logger;

        public LocalStorage(ILogger<LocalStorage> logger, IOptions<OutputConfiguration> configuration)
        {
            CreateStoragePath(configuration.Value.DataPath);
            this.DataPath = configuration.Value.DataPath;
            this.logger = logger;
        }

        private static void CreateStoragePath(string path)
        {
            if (false == Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        public async Task<string> GetEnrollmentProofAsync(Guid accountId, CancellationToken cancellationToken)
        {
            var path = Path.Combine(this.DataPath, $"enrollment-{accountId:N}");
            if (!File.Exists(path))
            {
                var temporaryPath = path + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    var proof = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
                    await WriteCredentialAsync(temporaryPath, proof, cancellationToken);
                    try { File.Move(temporaryPath, path); }
                    catch (IOException) when (File.Exists(path)) { }
                }
                finally { File.Delete(temporaryPath); }
            }
            return await File.ReadAllTextAsync(path, cancellationToken);
        }

        private static async Task WriteCredentialAsync(string path, string value, CancellationToken cancellationToken)
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using var stream = new FileStream(path, options);
            await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(value), cancellationToken);
            stream.Flush(flushToDisk: true);
        }

        public async Task StoreTokenAsync(string token, CancellationToken cancellationToken)
        {
            string path = Path.Join(this.DataPath, this.TokenFileName);
            var temporaryPath = path + Guid.NewGuid().ToString("N") + ".tmp";
            LocalStorageLog.StoreToken(logger, path);
            try
            {
                await WriteCredentialAsync(temporaryPath, token, cancellationToken);
                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                File.Delete(temporaryPath);
            }
        }

        public async Task<string?> RetrieveTokenAsync(CancellationToken cancellationToken)
        {
            string path = Path.Join(this.DataPath, this.TokenFileName);
            if (false == File.Exists(path))
            {
                LocalStorageLog.TokenFileDoesNotExist(logger, path);
                return null;
            }

            LocalStorageLog.RetrievedToken(logger, path);
            return await File.ReadAllTextAsync(path, cancellationToken);
        }

        public Task DeleteTokenAsync(CancellationToken cancellationToken)
        {
            string path = Path.Join(this.DataPath, this.TokenFileName);
            if (File.Exists(path))
            {
                LocalStorageLog.DeleteExistingToken(logger, path);
                File.Delete(path);
            }

            return Task.CompletedTask;
        }
    }

    internal sealed partial class LocalStorageLog
    {
        [LoggerMessage(LogLevel.Debug, "An existing token file at \"{Path}\" will be deleted.")]
        public static partial void DeleteExistingToken(ILogger logger, string path);

        [LoggerMessage(LogLevel.Debug, "Creating a token file at \"{Path}\".")]
        public static partial void StoreToken(ILogger logger, string path);

        [LoggerMessage(LogLevel.Debug, "Retrieved a token file at \"{Path}\".")]
        public static partial void RetrievedToken(ILogger logger, string path);

        [LoggerMessage(LogLevel.Debug, "A token file at \"{Path}\" does not exist.")]
        public static partial void TokenFileDoesNotExist(ILogger logger, string path);
    }
}

