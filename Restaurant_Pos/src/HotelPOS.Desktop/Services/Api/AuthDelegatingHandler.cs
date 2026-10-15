using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HotelPOS.Contracts.Common;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Configuration;

namespace HotelPOS.Desktop.Services.Api;

public sealed class AuthDelegatingHandler : DelegatingHandler
{
    public static readonly HttpRequestOptionsKey<bool> SkipAuthentication = new("HotelPOS.SkipAuthentication");

    private readonly ITokenRefresher _refresher;
    private readonly IClientSettingsService _settings;
    private readonly IAuthSession _session;

    public AuthDelegatingHandler(ITokenRefresher refresher, IClientSettingsService settings, IAuthSession session)
    {
        _refresher = refresher;
        _settings = settings;
        _session = session;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        AddClientHeaders(request);

        if (request.Options.TryGetValue(SkipAuthentication, out var skip) && skip)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var token = await _refresher.GetValidAccessTokenAsync(cancellationToken);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized || token is null)
        {
            await EndSessionIfAccessRevokedAsync(response, cancellationToken);
            return response;
        }

        var refreshed = await _refresher.RefreshAsync(token, cancellationToken);
        if (refreshed is null)
        {
            return response;
        }

        response.Dispose();
        using var retry = Clone(request);
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed);
        var retried = await base.SendAsync(retry, cancellationToken);
        await EndSessionIfAccessRevokedAsync(retried, cancellationToken);
        return retried;
    }

    // An administrator disabled this terminal or this account while it was in use: every further call would be
    // refused, so sign out with the reason instead of leaving the user on a screen where nothing works.
    private async Task EndSessionIfAccessRevokedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode != HttpStatusCode.Forbidden
            || response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
        {
            return;
        }

        // Buffered so ApiClient can still read the body afterwards.
        await response.Content.LoadIntoBufferAsync();
        try
        {
            var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(PosJson.Options, cancellationToken);
            var error = envelope?.Errors.FirstOrDefault(e => e.Code is ErrorCodes.DeviceDisabled or ErrorCodes.AccountDisabled);
            if (error is not null)
            {
                _session.Expire(error.Message);
            }
        }
        catch (JsonException)
        {
            // Not an API envelope: ApiClient reports it.
        }
    }

    private void AddClientHeaders(HttpRequestMessage request)
    {
        var settings = _settings.Current;
        if (settings.DeviceId is { } deviceId && !request.Headers.Contains(PosHeaders.DeviceId))
        {
            request.Headers.Add(PosHeaders.DeviceId, deviceId.ToString());
        }

        if (!request.Headers.Contains(PosHeaders.MachineName))
        {
            request.Headers.Add(PosHeaders.MachineName, Environment.MachineName);
        }

        if (!request.Headers.Contains(PosHeaders.CorrelationId))
        {
            request.Headers.Add(PosHeaders.CorrelationId, Guid.NewGuid().ToString("N"));
        }
    }

    private static HttpRequestMessage Clone(HttpRequestMessage original)
    {
        // Request content is not disposed by HttpClient (.NET Core 3+), and JsonContent can be sent again.
        var clone = new HttpRequestMessage(original.Method, original.RequestUri)
        {
            Content = original.Content,
            Version = original.Version,
            VersionPolicy = original.VersionPolicy,
        };

        foreach (var header in original.Headers.Where(h => h.Key != "Authorization"))
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in original.Options)
        {
            ((IDictionary<string, object?>)clone.Options)[option.Key] = option.Value;
        }

        return clone;
    }
}
