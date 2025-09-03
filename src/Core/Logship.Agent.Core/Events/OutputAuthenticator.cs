// <copyright file="OutputAuthenticator.cs" company="Logship LLC">
// Copyright (c) Logship LLC. All rights reserved.
// </copyright>

using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Internals;
using Logship.Agent.Core.Internals.Models;
using Logship.Agent.Core.Services;
using Logship.Agent.DeviceIdentifier.Formatter;
using Logship.DeviceIdentifier;
using Logship.DeviceIdentifier.Components;
using Logship.DeviceIdentifier.Components.Linux;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;

namespace Logship.Agent.Core.Events
{
    public sealed class OutputAuthenticator : IOutputAuth, IRefreshAuth, IDisposable
    {
        private readonly string endpoint;
        private readonly Guid accountId;
        private readonly ITokenStorage tokenStorage;
        private readonly IHttpClientFactory httpClient;
        private readonly ILogger<OutputAuthenticator> logger;
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1);
        private string? deviceId;

        private string? accessToken;
        private DateTime? refreshAt;
        private string? refreshToken;

        public OutputAuthenticator(IOptions<OutputConfiguration> config, ITokenStorage tokenStorage, IHttpClientFactory httpClient, ILogger<OutputAuthenticator> logger)
        {
            this.endpoint = config.Value.Endpoint;
            this.accountId = config.Value.Account;
            this.tokenStorage = tokenStorage;
            this.httpClient = httpClient;
            this.logger = logger;

            this.deviceId = null;
        }

        public async ValueTask<bool> TryAddAuthAsync(HttpRequestMessage requestMessage, CancellationToken token)
        {
            if (this.RequiresRefresh)
            {
                await this.RefreshAsync(token);
                if (string.IsNullOrEmpty(accessToken))
                {
                    AuthenticatorLog.NoAccessToken(logger);
                    return false;
                }
            }

            requestMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            return true;
        }

        private bool RequiresRefresh => string.IsNullOrEmpty(accessToken)
                || string.IsNullOrEmpty(refreshToken)
                || refreshAt == null
                || refreshAt < DateTime.UtcNow;

        public async Task<(string? refreshToken, string? accessToken)> RefreshAsync(CancellationToken token)
        {
            if (this.deviceId == null)
            {
                var deviceHash = new DeviceIdentifierBuilder();
                await deviceHash.AddAspectsAsync(
                [
                    new MacAddressAspect(),
                    new HostNameAspect(),
                    new OperatingSystemAspect(),
                    new DockerContainerIdAspect(),
                ], token);
                this.deviceId = deviceHash.Build(new Sha512Formatter());
            }

            // Add the new log message here
            AuthenticatorLog.DeviceVerificationId(logger, this.deviceId);
            if (this.RequiresRefresh)
            {
                try
                {
                    if (false == this.semaphore.Wait(0, token))
                    {
                        await this.semaphore.WaitAsync(token);
                    }

                    if (this.RequiresRefresh)
                    {

                        AuthenticatorLog.AccessToken(logger);

                        // Create refresh model with machine information
                        var model = new AgentRefreshRequestModel(
                            Environment.MachineName,
                            Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName,
                            deviceId,
                            [
                                new KeyValuePair<string, string>("os", System.Runtime.InteropServices.RuntimeInformation.OSDescription),
                                new KeyValuePair<string, string>("os_version", System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString()),
                            ]);

                        AgentRefreshResponseModel? content = null;
                        using var client = this.httpClient.CreateClient(nameof(OutputAuthenticator));

                        while (token.IsCancellationRequested == false)
                        {
                            using var request = await Api.PostAgentRefreshAsync(endpoint, this.accountId, model, token);
                            request.Headers.Add("x-ls-agent-deviceid", this.deviceId);

                            if (this.refreshToken != null)
                            {
                                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", this.refreshToken);
                            }

                            try
                            {
                                using var result = await client.SendAsync(request, token);
                                // Handle 401 with new refresh token
                                if (!result.IsSuccessStatusCode)
                                {
                                    if (result.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                                    {
                                        var unauthorizedContent = await result.Content.ReadFromJsonAsync(ModelSourceGenerationContext.Default.AgentRefreshResponseModel, token);
                                        if (unauthorizedContent != null && !string.IsNullOrEmpty(unauthorizedContent.RefreshToken))
                                        {
                                            AuthenticatorLog.RefreshingWithNewToken(logger);
                                            this.refreshToken = unauthorizedContent.RefreshToken;
                                            this.accessToken = null;
                                            AuthenticatorLog.DeviceIdentifier(logger, this.deviceId);
                                            await Task.Delay(15_000, token);
                                            continue;
                                        }
                                    }

                                    result.EnsureSuccessStatusCode(); // Will throw for other errors
                                }

                                content = await result.Content.ReadFromJsonAsync(ModelSourceGenerationContext.Default.AgentRefreshResponseModel, token);
                                ArgumentNullException.ThrowIfNull(content, nameof(content));
                            }
                            catch (OperationCanceledException) when (token.IsCancellationRequested)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                AuthenticatorLog.AccessTokenError(logger, ex);
                            }

                            if (content != null)
                            {
                                break;
                            }

                            await Task.Delay(15_000, token);
                        }

                        this.accessToken = content!.AccessToken;
                        this.refreshToken = content.RefreshToken;
                        await this.tokenStorage.StoreTokenAsync(this.refreshToken, token);
                        if (false == string.IsNullOrEmpty(this.accessToken))
                        {
                            var handler = new JwtSecurityTokenHandler();
                            var jwtSecurityToken = handler.ReadJwtToken(this.accessToken);

                            var now = DateTime.UtcNow;
                            this.refreshAt = now + ((jwtSecurityToken.ValidTo - now).Duration() / 2);
                        }
                        else
                        {
                            this.refreshAt = null;
                        }
                    }
                }
                catch (Exception ex)
                {
                    AuthenticatorLog.AccessTokenError(logger, ex);
                    this.accessToken = null;
                    throw;
                }
                finally
                {
                    this.semaphore.Release();
                }
            }

            return (this.refreshToken, this.accessToken);
        }

        public async Task Invalidate(CancellationToken token)
        {
            try
            {
                if (false == this.semaphore.Wait(0, token))
                {
                    await this.semaphore.WaitAsync(token);
                }

                this.accessToken = null;
            }
            finally { this.semaphore.Release(); }
        }

        public void Dispose()
        {
            ((IDisposable)semaphore).Dispose();
        }

        public ValueTask InvalidateAsync(CancellationToken token)
        {
            this.accessToken = null;
            return ValueTask.CompletedTask;
        }

        public async Task SetTokens(string? refreshToken, string? accessToken, CancellationToken token)
        {
            try
            {
                if (false == this.semaphore.Wait(0, token))
                {
                    await this.semaphore.WaitAsync(token);
                }

                this.refreshToken = refreshToken;
                this.accessToken = accessToken;
            }
            finally { this.semaphore.Release(); }
        }

        public Task<(string? refreshToken, string? accessToken)> GetTokensAsync(CancellationToken token)
        {
            return Task.FromResult((this.refreshToken, this.accessToken));
        }
    }

    internal sealed partial class AuthenticatorLog
    {
        [LoggerMessage(LogLevel.Error, "Failed to get access token.")]
        public static partial void AccessTokenError(ILogger logger, Exception ex);

        [LoggerMessage(LogLevel.Information, "Refreshing access token.")]
        public static partial void AccessToken(ILogger logger);

        [LoggerMessage(LogLevel.Warning, "No access token resolved. Agent requires permissions for upload.")]
        public static partial void NoAccessToken(ILogger logger);

        [LoggerMessage(LogLevel.Information, "Using device identifier: {deviceId}")]
        public static partial void DeviceIdentifier(ILogger logger, string deviceId);

        [LoggerMessage(LogLevel.Information, "Received new refresh token. Attempting to refresh with it.")]
        public static partial void RefreshingWithNewToken(ILogger logger);

        [LoggerMessage(LogLevel.Information, "Device verification ID: {deviceId}")]
        public static partial void DeviceVerificationId(ILogger logger, string deviceId);
    }
}

