using HotelPOS.Contracts.Auth;
using HotelPOS.Desktop.Services.Configuration;

namespace HotelPOS.Desktop.Services.Auth;

/// <summary>The logged-in user and their tokens. The access token lives in memory only.</summary>
public interface IAuthSession
{
    bool IsAuthenticated { get; }

    CurrentUserDto? User { get; }

    string? AccessToken { get; }

    DateTime AccessTokenExpiresAtUtc { get; }

    string? RefreshToken { get; }

    /// <summary>Raised (on any thread) when the server rejected the session; the argument is the reason.</summary>
    event EventHandler<string>? Expired;

    event EventHandler? Changed;

    void Start(LoginResponse login);

    void Update(LoginResponse refreshed);

    void Clear();

    void Expire(string reason);

    void MarkPasswordChanged();

    bool HasAnyRole(IEnumerable<string> roles);

    /// <summary>Refresh token kept from a previous run (DPAPI-protected), used to resume after a restart.</summary>
    string? LoadPersistedRefreshToken();
}

public sealed class AuthSession : IAuthSession
{
    private const string RefreshTokenItem = "session";

    private readonly ISecureStore _secureStore;
    private readonly object _gate = new();
    private LoginResponse? _login;

    public AuthSession(ISecureStore secureStore)
    {
        _secureStore = secureStore;
    }

    public event EventHandler<string>? Expired;

    public event EventHandler? Changed;

    public bool IsAuthenticated
    {
        get
        {
            lock (_gate)
            {
                return _login is not null;
            }
        }
    }

    public CurrentUserDto? User
    {
        get
        {
            lock (_gate)
            {
                return _login?.User;
            }
        }
    }

    public string? AccessToken
    {
        get
        {
            lock (_gate)
            {
                return _login?.AccessToken;
            }
        }
    }

    public DateTime AccessTokenExpiresAtUtc
    {
        get
        {
            lock (_gate)
            {
                return _login?.AccessTokenExpiresAtUtc ?? DateTime.MinValue;
            }
        }
    }

    public string? RefreshToken
    {
        get
        {
            lock (_gate)
            {
                return _login?.RefreshToken;
            }
        }
    }

    public void Start(LoginResponse login) => Set(login);

    public void Update(LoginResponse refreshed) => Set(refreshed);

    public void Clear()
    {
        lock (_gate)
        {
            _login = null;
        }

        _secureStore.Delete(RefreshTokenItem);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Expire(string reason)
    {
        if (!IsAuthenticated)
        {
            return;
        }

        Expired?.Invoke(this, reason);
    }

    public void MarkPasswordChanged()
    {
        lock (_gate)
        {
            if (_login is not null)
            {
                _login = _login with { User = _login.User with { MustChangePassword = false } };
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool HasAnyRole(IEnumerable<string> roles)
    {
        var user = User;
        return user is not null && roles.Any(r => user.Roles.Contains(r));
    }

    public string? LoadPersistedRefreshToken() => _secureStore.Load(RefreshTokenItem);

    private void Set(LoginResponse login)
    {
        lock (_gate)
        {
            _login = login;
        }

        _secureStore.Save(RefreshTokenItem, login.RefreshToken);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
