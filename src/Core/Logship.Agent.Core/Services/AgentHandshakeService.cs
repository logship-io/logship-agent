using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Logship.Agent.Core.Internals;
using Logship.Agent.Core.Internals.Models;
using Logship.Agent.DeviceIdentifier.Formatter;
using Logship.DeviceIdentifier;
using Logship.DeviceIdentifier.Components;
using Logship.DeviceIdentifier.Components.Linux;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;

namespace Logship.Agent.Core.Services
{
    public sealed class AgentHandshakeService
    {
        private readonly ITokenStorage tokenStorage;
        private readonly IOptions<OutputConfiguration> config;
        private readonly IHandshakeAuth handshakeAuthenticator;
        private readonly ILogger<AgentHandshakeService> logger;
        private readonly IHttpClientFactory clientFactory;
        private string deviceId = string.Empty;

        public AgentHandshakeService(ITokenStorage tokenStorage, IOptions<OutputConfiguration> config, IHandshakeAuth handshakeAuthenticator, IHttpClientFactory httpClientFactory, ILogger<AgentHandshakeService> logger)
        {
            this.tokenStorage = tokenStorage;
            this.config = config;
            this.handshakeAuthenticator = handshakeAuthenticator;
            this.logger = logger;
            this.clientFactory = httpClientFactory;
        }

        public async Task PerformHandshakeAsync(CancellationToken cancellationToken)
        {
            var delay = TimeSpan.FromSeconds(5);

            var deviceHash = new DeviceIdentifierBuilder();
            await deviceHash.AddAspectsAsync(
            [
                new MacAddressAspect(),
                new HostNameAspect(),
                new OperatingSystemAspect(),
                new DockerContainerIdAspect(),
            ], cancellationToken);
            this.deviceId = deviceHash.Build(new Sha512Formatter());

            AgentHandshakeServiceLog.AgentHandshake(this.logger);
            while (false == cancellationToken.IsCancellationRequested)
            {
                if (await this.RegisterWithStoredTokenAsync(cancellationToken))
                {
                    break;
                }

                if (await this.RegisterWithRegistrationTokenAsync(cancellationToken))
                {
                    break;
                }

                if (await this.RegisterWithManualApprovalAsync(cancellationToken))
                {
                    break;
                }

                AgentHandshakeServiceLog.FailedHandshake(logger, delay);
                await Task.Delay(delay, cancellationToken);
                if (delay < TimeSpan.FromMinutes(5))
                {
                    delay *= 2;
                }
            }

            AgentHandshakeServiceLog.SuccessfulHandshake(this.logger);
        }

        private async Task<bool> RegisterWithManualApprovalAsync(CancellationToken token)
        {
            if (false == await RegisterAgentAsync(null, token))
            {
                AgentHandshakeServiceLog.FailedHandshakeWithManualApproval(this.logger);
                return false;
            }

            if (false == await RegisterWithStoredTokenAsync(token))
            {
                AgentHandshakeServiceLog.FailedRefreshWithManualApproval(this.logger);
                return false;
            }

            return true;
        }

        private async Task<bool> RegisterWithRegistrationTokenAsync(CancellationToken token)
        {
            string? regToken = this.config.Value.Registration?.RegistrationToken;
            if (string.IsNullOrWhiteSpace(regToken))
            {
                return false;
            }

            if (false == await RegisterAgentAsync(regToken, token))
            {
                AgentHandshakeServiceLog.FailedHandshakeWithRegistrationToken(this.logger);
                return false;
            }

            var refreshToken = await this.tokenStorage.RetrieveTokenAsync(token);
            if (string.IsNullOrEmpty(refreshToken)
                || false == await RefreshWithTokenAsync(refreshToken, token))
            {
                AgentHandshakeServiceLog.FailedRefreshWithRegistrationToken(this.logger);
                return false;
            }

            return true;
        }

        private async Task<bool> RegisterWithStoredTokenAsync(CancellationToken token)
        {
            var refreshToken = await this.tokenStorage.RetrieveTokenAsync(token);
            if (refreshToken != null)
            {
                var handler = new JwtSecurityTokenHandler();
                try
                {
                    var jwtSecurityToken = handler.ReadJwtToken(refreshToken);
                    if (DateTime.UtcNow >= jwtSecurityToken.ValidTo)
                    {
                        refreshToken = null;
                        await this.tokenStorage.DeleteTokenAsync(token);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    AgentHandshakeServiceLog.FailedDeserializeStoredToken(logger, ex);
                    await this.tokenStorage.DeleteTokenAsync(token);
                    return false;
                }
            }

            if (string.IsNullOrEmpty(refreshToken))
            {
                return false;
            }

            return await RefreshWithTokenAsync(refreshToken, token);
        }

        private async Task<bool> RefreshWithTokenAsync(string refreshToken, CancellationToken token)
        {
            using var client = this.clientFactory.CreateClient(nameof(AgentHandshakeService));
            using var request = await Api.GetRefreshTokensAsync(config.Value.Endpoint, config.Value.Account, token);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", refreshToken);
            try
            {
                using var result = await client.SendAsync(request, token);
                if (result.IsSuccessStatusCode)
                {
                    var content = await result.Content.ReadFromJsonAsync(ModelSourceGenerationContext.Default.AgentRefreshResponseModel, token);
                    await this.handshakeAuthenticator.SetInitialToken(content!.RefreshToken, token);

                    if (false == string.IsNullOrWhiteSpace(content.AccessToken))
                    {
                        return true;
                    }

                    return false;
                }
                else
                {
                    AgentHandshakeServiceLog.FailedRefreshWithToken(this.logger, (int)result.StatusCode, result.StatusCode.ToString("g"));
                }
            }
            catch (Exception ex)
            {
                AgentHandshakeServiceLog.FailedHandshakeWithToken(logger, ex);
            }

            return false;
        }

        private async Task<bool> RegisterAgentAsync(string? registrationToken, CancellationToken token)
        {
            using var client = this.clientFactory.CreateClient(nameof(AgentHandshakeService));
            var name = System.Environment.MachineName;
            var model = new Internals.Models.AgentRegistrationRequestModel(name, name, deviceId, []);
            using var registerRequest = await Api.PostAgentHandshakeAsync(this.config.Value.Endpoint, this.config.Value.Account, model, token);
            if (false == string.IsNullOrWhiteSpace(registrationToken))
            {
                registerRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {registrationToken}");
            }

            try
            {
                using var result = await client.SendAsync(registerRequest, token);
                if (false == result.IsSuccessStatusCode)
                {
                    AgentHandshakeServiceLog.LogAgentHandshakeErrorResponse(logger, (int)result.StatusCode, result.StatusCode.ToString("g"));
                    return false;
                }
                else
                {
                    var response = await result.Content.ReadFromJsonAsync(ModelSourceGenerationContext.Default.AgentRegistrationResponseModel, token);
                    if (response == null)
                    {
                        AgentHandshakeServiceLog.LogAgentHandshakeResponseDeserializeError(logger, (int)result.StatusCode, result.StatusCode.ToString("g"));
                        return false;
                    }

                    await this.handshakeAuthenticator.SetInitialToken(response.HandshakeToken, token);
                    return true;
                }
            }
            catch (Exception ex)
            {
                AgentHandshakeServiceLog.LogAgentHandshakeException(this.logger, ex);
                return false;
            }
        }
    }

    internal static partial class AgentHandshakeServiceLog
    {
        [LoggerMessage(LogLevel.Warning, "Duplicate StartAsync call.")]
        public static partial void LogDuplicateStart(ILogger logger);

        [LoggerMessage(LogLevel.Information, "Executing agent handshake.")]
        public static partial void AgentHandshake(ILogger logger);

        [LoggerMessage(LogLevel.Warning, "Agent handshake had an unsuccessful response code {StatusCode}: {Message}")]
        public static partial void LogAgentHandshakeErrorResponse(ILogger logger, int statusCode, string message);

        [LoggerMessage(LogLevel.Warning, "Unable to deserialize agent handshake response. {StatusCode}: {Message}")]
        public static partial void LogAgentHandshakeResponseDeserializeError(ILogger logger, int statusCode, string message);

        [LoggerMessage(LogLevel.Information, "Agent handshake completed successfully.")]
        public static partial void SuccessfulHandshake(ILogger logger);

        [LoggerMessage(LogLevel.Warning, "Agent handshake failed. Next handshake attempt in {Delay}")]
        public static partial void FailedHandshake(ILogger logger, TimeSpan delay);

        [LoggerMessage(LogLevel.Error, "Exception during agent handshake.")]
        public static partial void LogAgentHandshakeException(ILogger logger, Exception ex);

        [LoggerMessage(LogLevel.Error, "Failed to deserialize stored token.")]
        public static partial void FailedDeserializeStoredToken(ILogger logger, Exception ex);

        [LoggerMessage(LogLevel.Error, "Exception during token handshake attempt.")]
        public static partial void FailedHandshakeWithToken(ILogger logger, Exception ex);

        [LoggerMessage(LogLevel.Warning, "Unable to refresh with token. {StatusCode}: {Message}")]
        public static partial void FailedRefreshWithToken(ILogger logger, int statusCode, string message);

        [LoggerMessage(LogLevel.Warning, "Refresh with configured registration token failed.")]
        public static partial void FailedRefreshWithRegistrationToken(ILogger logger);

        [LoggerMessage(LogLevel.Warning, "Handshake with configured registration token failed.")]
        public static partial void FailedHandshakeWithRegistrationToken(ILogger logger);

        [LoggerMessage(LogLevel.Warning, "Handshake with manual approval failed.")]
        public static partial void FailedHandshakeWithManualApproval(ILogger logger);

        [LoggerMessage(LogLevel.Warning, "Refresh with manual approval failed.")]
        public static partial void FailedRefreshWithManualApproval(ILogger logger);
    }
}
