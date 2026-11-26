using Logship.Agent.Core.Configuration;
using Logship.Agent.Core.Events;
using Logship.Agent.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;

namespace Logship.Agent.FileBasedTests.Utility;

internal sealed class TestAgentBackend : HttpMessageHandler, IHttpClientFactory, IRefreshAuth, ITokenStorage
{
    public Func<CancellationToken, Task<HttpStatusCode>> Upload { get; set; } =
        _ => Task.FromResult(HttpStatusCode.OK);
    public int UploadCount { get; private set; }
    public Func<CancellationToken, Task<(string? RefreshToken, string? AccessToken)>> Refresh { get; set; } =
        _ => Task.FromResult<(string?, string?)>(("refresh", "access"));

    public AgentAuthenticationService CreateAuthentication(IOptions<OutputConfiguration> options) =>
        new(this, options, this, this, NullLogger<AgentAuthenticationService>.Instance);

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        UploadCount++;
        return new HttpResponseMessage(await Upload(cancellationToken)) { RequestMessage = request };
    }

    public Task<(string? refreshToken, string? accessToken)> GetTokensAsync(CancellationToken token) =>
        Task.FromResult<(string?, string?)>(("refresh", "access"));
    public Task<(string? refreshToken, string? accessToken)> RefreshAsync(CancellationToken token) => Refresh(token);
    public Task SetTokens(string? refreshToken, string? accessToken, CancellationToken token) => Task.CompletedTask;
    public ValueTask<bool> TryAddAuthAsync(HttpRequestMessage requestMessage, CancellationToken token) => ValueTask.FromResult(true);
    public ValueTask InvalidateAsync(CancellationToken token) => ValueTask.CompletedTask;
    public Task<string?> RetrieveTokenAsync(CancellationToken cancellationToken) => Task.FromResult<string?>("refresh");
    public Task<string> GetEnrollmentProofAsync(Guid accountId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task StoreTokenAsync(string token, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task DeleteTokenAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
}
