namespace Logship.Agent.Core.Events
{
    public interface IRefreshAuth : IOutputAuth
    {
        Task<(string? refreshToken, string? accessToken)> GetTokensAsync(CancellationToken token);
        Task SetTokens(string? refreshToken, string? accessToken, CancellationToken token);
        Task<(string? refreshToken, string? accessToken)> RefreshAsync(CancellationToken token);
    }
}
