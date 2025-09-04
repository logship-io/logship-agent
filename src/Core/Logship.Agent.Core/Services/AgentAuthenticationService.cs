// <copyright file="AgentRefreshService.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Logship.Agent.Core.Services
{
    public sealed class AgentAuthenticationService
    {
        private readonly ITokenStorage tokenStorage;
        private readonly IOptions<OutputConfiguration> config;
        private readonly IRefreshAuth refreshAuthenticator;
        private readonly ILogger<AgentAuthenticationService> logger;
        private readonly IHttpClientFactory clientFactory;

        private string? registrationToken = null;
        private string deviceId = string.Empty;

        public AgentAuthenticationService(ITokenStorage tokenStorage, IOptions<OutputConfiguration> config, IRefreshAuth refreshAuthenticator, IHttpClientFactory httpClientFactory, ILogger<AgentAuthenticationService> logger)
        {
            this.tokenStorage = tokenStorage;
            this.config = config;
            this.refreshAuthenticator = refreshAuthenticator;
            this.logger = logger;
            this.clientFactory = httpClientFactory;
            this.registrationToken = config.Value.Registration?.RegistrationToken;
        }

        public async Task PerformRefreshAsync(CancellationToken cancellationToken)
        {
            var delay = TimeSpan.FromSeconds(5);
            string? token = null;
            if (false == string.IsNullOrWhiteSpace(this.registrationToken))
            {
                token = this.registrationToken;
                this.registrationToken = null; // Clear the registration token after use
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                token = await this.tokenStorage.RetrieveTokenAsync(cancellationToken);
            }

            if (false == string.IsNullOrWhiteSpace(token))
            {
                await this.refreshAuthenticator.SetTokens(token, null, cancellationToken);
            }

            AgentRefreshServiceLog.AgentRefresh(this.logger);
            while (false == cancellationToken.IsCancellationRequested)
            {
                await this.refreshAuthenticator.RefreshAsync(cancellationToken);
                var (_, accessToken) = await this.refreshAuthenticator.GetTokensAsync(cancellationToken);
                if (false == string.IsNullOrWhiteSpace(accessToken))
                {
                    return;
                }

                AgentRefreshServiceLog.FailedRefresh(logger, delay);
                await Task.Delay(delay, cancellationToken);
                if (delay < TimeSpan.FromMinutes(5))
                {
                    delay *= 2;
                }
            }

            AgentRefreshServiceLog.SuccessfulRefresh(this.logger);
        }
    }

    internal static partial class AgentRefreshServiceLog
    {
        [LoggerMessage(LogLevel.Warning, "Duplicate StartAsync call.")]
        public static partial void LogDuplicateStart(ILogger logger);

        [LoggerMessage(LogLevel.Information, "Executing agent refresh.")]
        public static partial void AgentRefresh(ILogger logger);

        [LoggerMessage(LogLevel.Warning, "Agent refresh had an unsuccessful response code {StatusCode}: {Message}")]
        public static partial void LogAgentRefreshErrorResponse(ILogger logger, int statusCode, string message);

        [LoggerMessage(LogLevel.Warning, "Unable to deserialize agent refresh response. {StatusCode}: {Message}")]
        public static partial void LogAgentRefreshResponseDeserializeError(ILogger logger, int statusCode, string message);

        [LoggerMessage(LogLevel.Information, "Agent refresh completed successfully.")]
        public static partial void SuccessfulRefresh(ILogger logger);

        [LoggerMessage(LogLevel.Trace, "Agent refresh failed. Next refresh attempt in {Delay}")]
        public static partial void FailedRefresh(ILogger logger, TimeSpan delay);

        [LoggerMessage(LogLevel.Error, "Exception during agent refresh.")]
        public static partial void LogAgentRefreshException(ILogger logger, Exception ex);

        [LoggerMessage(LogLevel.Error, "Failed to deserialize stored token.")]
        public static partial void FailedDeserializeStoredToken(ILogger logger, Exception ex);

        [LoggerMessage(LogLevel.Error, "Exception during token refresh attempt.")]
        public static partial void FailedRefreshWithToken(ILogger logger, Exception ex);

        [LoggerMessage(LogLevel.Warning, "Unable to refresh with token. {StatusCode}: {Message}")]
        public static partial void FailedRefreshWithToken(ILogger logger, int statusCode, string message);
    }
}

