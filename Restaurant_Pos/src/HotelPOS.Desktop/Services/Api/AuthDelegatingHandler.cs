using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using HotelPOS.Contracts.Common;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Configuration;

namespace HotelPOS.Desktop.Services.Api;

public sealed class AuthDelegatingHandler : DelegatingHandler
{
    public static readonly HttpRequestOptionsKey<bool> SkipAuthentication = new("HotelPOS.SkipAuthentication");

    private readonly ITokenRefresher _refresher;
    private readonly IClientSettingsService _settings;

    public AuthDelegatingHandler(ITokenRefresher refresher, IClientSettingsService settings)
    {
        _refresher = refresher;
        _settings = settings;
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
        return await base.SendAsync(retry, cancellationToken);
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
