// <copyright file="LocalStorage.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO;

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

        public async Task StoreTokenAsync(string token, CancellationToken cancellationToken)
        {
            string path = Path.Join(this.DataPath, this.TokenFileName);
            await this.DeleteTokenAsync(cancellationToken);
            LocalStorageLog.StoreToken(logger, path);
            await File.WriteAllTextAsync(path, token, cancellationToken);
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
        [LoggerMessage(LogLevel.Information, "An existing token file at \"{Path}\" will be deleted.")]
        public static partial void DeleteExistingToken(ILogger logger, string path);

        [LoggerMessage(LogLevel.Information, "Creating a token file at \"{Path}\".")]
        public static partial void StoreToken(ILogger logger, string path);

        [LoggerMessage(LogLevel.Information, "Retrieved a token file at \"{Path}\".")]
        public static partial void RetrievedToken(ILogger logger, string path);

        [LoggerMessage(LogLevel.Information, "A token file at \"{Path}\" does not exist.")]
        public static partial void TokenFileDoesNotExist(ILogger logger, string path);
    }
}

