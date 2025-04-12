using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Internals;
using Logship.Agent.Core.Internals.Models;
using Logship.Agent.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;

namespace Logship.Agent.Core.Events
{
    public sealed class OutputAuthenticator : IOutputAuth, IHandshakeAuth, IDisposable
    {
        private readonly string endpoint;
        private readonly Guid subscriptionId;
        private readonly ITokenStorage tokenStorage;
        private readonly IHttpClientFactory httpClient;
        private readonly ILogger<OutputAuthenticator> logger;
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1);

        private string? accessToken;
        private DateTime? refreshAt;
        private string? refreshToken;

        public OutputAuthenticator(IOptions<OutputConfiguration> config, ITokenStorage tokenStorage, IHttpClientFactory httpClient, ILogger<OutputAuthenticator> logger)
        {
            this.endpoint = config.Value.Endpoint;
            this.subscriptionId = config.Value.Account;
            this.tokenStorage = tokenStorage;
            this.httpClient = httpClient;
            this.logger = logger;
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
                || refreshAt == null
                || refreshAt < DateTime.UtcNow;

        private async Task RefreshAsync(CancellationToken token)
        {
            if (this.RequiresRefresh)
            {
                try
                {
                    if (false == this.semaphore.Wait(0, token))
                    {
                        await this.semaphore.WaitAsync(token);
                    }

                    if (this.refreshToken == null)
                    {
                        throw new InvalidOperationException("Refresh token is null, authentication is not initialized.");
                    }

                    if (this.RequiresRefresh)
                    {
                        AuthenticatorLog.AccessToken(logger);
                        using var request = await Api.GetRefreshTokensAsync(endpoint, this.subscriptionId, token);
                        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", this.refreshToken);
                        using var client = this.httpClient.CreateClient(nameof(OutputAuthenticator));
                        using var result = await client.SendAsync(request, token);
                        result.EnsureSuccessStatusCode();

                        var content = await result.Content.ReadFromJsonAsync(ModelSourceGenerationContext.Default.AgentRefreshResponseModel, token);
                        ArgumentNullException.ThrowIfNull(content, nameof(content));
                        this.accessToken = content.AccessToken;
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

        public Task SetInitialToken(string refreshToken, CancellationToken token)
        {
            this.refreshToken = refreshToken;
            return this.tokenStorage.StoreTokenAsync(refreshToken, token);
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
    }

    internal sealed partial class AuthenticatorLog
    {
        [LoggerMessage(LogLevel.Error, "Failed to get access token.")]
        public static partial void AccessTokenError(ILogger logger, Exception ex);

        [LoggerMessage(LogLevel.Information, "Refreshing access token.")]
        public static partial void AccessToken(ILogger logger);

        [LoggerMessage(LogLevel.Warning, "No access token resolved. Agent requires permissions for upload.")]
        public static partial void NoAccessToken(ILogger logger);
    }
}
