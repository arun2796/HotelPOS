using HotelPOS.Desktop.Services.Api;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Desktop.Services.Auth;

public interface ITokenRefresher
{
    Task<string?> GetValidAccessTokenAsync(CancellationToken cancellationToken = default);

    Task<string?> RefreshAsync(string? rejectedToken, CancellationToken cancellationToken = default);
}

public sealed class TokenRefresher : ITokenRefresher
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(1);

    private readonly IAuthSession _session;
    private readonly IAuthApi _authApi;
    private readonly ILogger<TokenRefresher> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public TokenRefresher(IAuthSession session, IAuthApi authApi, ILogger<TokenRefresher> logger)
    {
        _session = session;
        _authApi = authApi;
        _logger = logger;
    }

    public async Task<string?> GetValidAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var token = _session.AccessToken;
        if (token is null)
        {
            return null;
        }

        if (_session.AccessTokenExpiresAtUtc - DateTime.UtcNow > RefreshMargin)
        {
            return token;
        }

        // Expiring soon: try to renew, but keep using the old token if the server is unreachable.
        return await RefreshAsync(token, cancellationToken) ?? _session.AccessToken;
    }

    public async Task<string?> RefreshAsync(string? rejectedToken, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Another request already refreshed while we were waiting.
            var current = _session.AccessToken;
            if (current is not null && current != rejectedToken && _session.AccessTokenExpiresAtUtc - DateTime.UtcNow > RefreshMargin)
            {
                return current;
            }

            var refreshToken = _session.RefreshToken;
            if (refreshToken is null)
            {
                return null;
            }

            var result = await _authApi.RefreshAsync(refreshToken, cancellationToken);
            if (result.Success && result.Data is not null)
            {
                _session.Update(result.Data);
                _logger.LogInformation("Session refreshed for {User}", result.Data.User.Username);
                return result.Data.AccessToken;
            }

            if (result.IsConnectionFailure)
            {
                // Not the user's fault: keep the session and let the connection manager recover.
                return null;
            }

            _logger.LogWarning("Session could not be refreshed: {Code}", result.ErrorCode);
            _session.Expire(string.IsNullOrWhiteSpace(result.Message) ? "Your session has expired. Please sign in again." : result.Message);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }
}
