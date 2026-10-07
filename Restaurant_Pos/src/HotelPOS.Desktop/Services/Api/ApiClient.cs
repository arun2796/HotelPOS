using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using HotelPOS.Contracts.Common;
using HotelPOS.Desktop.Services.Configuration;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Desktop.Services.Api;

public sealed record ApiRequestOptions
{
    /// <summary>Do not attach the access token (login, refresh, logout, system info).</summary>
    public bool Anonymous { get; init; }

    /// <summary>Sent as Idempotency-Key so a retried critical request is processed only once (Phase 4).</summary>
    public Guid? IdempotencyKey { get; init; }

    /// <summary>Talk to another server than the configured one ("Test connection" before saving).</summary>
    public string? BaseUrlOverride { get; init; }

    /// <summary>Shorter limit than the client default (15 s), for quick checks.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>GET requests are retried on connection failures unless this is false.</summary>
    public bool RetryReads { get; init; } = true;
}

public interface IApiClient
{
    Task<ApiResult<T>> GetAsync<T>(string path, CancellationToken cancellationToken = default, ApiRequestOptions? options = null);

    Task<ApiResult<T>> PostAsync<T>(string path, object? body, CancellationToken cancellationToken = default, ApiRequestOptions? options = null);

    Task<ApiResult<T>> PutAsync<T>(string path, object? body, CancellationToken cancellationToken = default, ApiRequestOptions? options = null);
}

/// <summary>
/// The only way the desktop talks to the server. Wraps HttpClient, unwraps the response envelope and
/// turns network problems into <see cref="ApiResult{T}.IsConnectionFailure"/> results instead of exceptions.
/// </summary>
public sealed class ApiClient : IApiClient
{
    public const string HttpClientName = "HotelPOS.Api";
    public const string ConnectionUnavailableMessage = "Connection unavailable. Check the Wi-Fi/LAN connection to the server.";

    private static readonly TimeSpan[] GetRetryDelays = { TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1500) };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IClientSettingsService _settings;
    private readonly ILogger<ApiClient> _logger;

    public ApiClient(IHttpClientFactory httpClientFactory, IClientSettingsService settings, ILogger<ApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
    }

    public async Task<ApiResult<T>> GetAsync<T>(string path, CancellationToken cancellationToken = default, ApiRequestOptions? options = null)
    {
        // Reads are safe to retry; writes are retried only with an idempotency key (from Phase 4).
        var result = await SendAsync<T>(HttpMethod.Get, path, null, options, cancellationToken);
        foreach (var delay in GetRetryDelays)
        {
            if (!result.IsConnectionFailure || cancellationToken.IsCancellationRequested || options?.RetryReads == false)
            {
                break;
            }

            await Task.Delay(delay, cancellationToken);
            result = await SendAsync<T>(HttpMethod.Get, path, null, options, cancellationToken);
        }

        return result;
    }

    public Task<ApiResult<T>> PostAsync<T>(string path, object? body, CancellationToken cancellationToken = default, ApiRequestOptions? options = null) =>
        SendAsync<T>(HttpMethod.Post, path, body, options, cancellationToken);

    public Task<ApiResult<T>> PutAsync<T>(string path, object? body, CancellationToken cancellationToken = default, ApiRequestOptions? options = null) =>
        SendAsync<T>(HttpMethod.Put, path, body, options, cancellationToken);

    private async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string path, object? body, ApiRequestOptions? options, CancellationToken cancellationToken)
    {
        var baseUrl = options?.BaseUrlOverride ?? _settings.Current.ApiBaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
        {
            return ApiResult<T>.ConnectionFailure("The server address is not configured.");
        }

        using var request = new HttpRequestMessage(method, new Uri(baseUri, path.TrimStart('/')));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: PosJson.Options);
        }

        if (options?.Anonymous == true)
        {
            request.Options.Set(AuthDelegatingHandler.SkipAuthentication, true);
        }

        if (options?.IdempotencyKey is { } key)
        {
            request.Headers.Add(PosHeaders.IdempotencyKey, key.ToString());
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (options?.Timeout is { } limit)
        {
            timeout.CancelAfter(limit);
        }

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, timeout.Token);
            return await ReadAsync<T>(response, method, path, timeout.Token);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("API {Method} {Path} failed: {Error}", method, path, ex.Message);
            return ApiResult<T>.ConnectionFailure(ConnectionUnavailableMessage);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("API {Method} {Path} timed out", method, path);
            return ApiResult<T>.ConnectionFailure("The server did not respond in time. Check the connection and try again.");
        }
    }

    private async Task<ApiResult<T>> ReadAsync<T>(HttpResponseMessage response, HttpMethod method, string path, CancellationToken cancellationToken)
    {
        ApiResponse<T>? envelope = null;
        try
        {
            if (response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
            {
                envelope = await response.Content.ReadFromJsonAsync<ApiResponse<T>>(PosJson.Options, cancellationToken);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "API {Method} {Path} returned an unreadable body (HTTP {Status})", method, path, (int)response.StatusCode);
        }

        if (envelope is null)
        {
            return ApiResult<T>.Fail(
                ErrorCodes.ServerError,
                $"Unexpected response from the server (HTTP {(int)response.StatusCode}). Check the server address.",
                response.StatusCode);
        }

        if (!envelope.Success)
        {
            _logger.LogInformation("API {Method} {Path} -> {Status} {Code} ({CorrelationId})",
                method, path, (int)response.StatusCode, envelope.Errors.FirstOrDefault()?.Code, envelope.CorrelationId);
        }

        return new ApiResult<T>
        {
            Success = envelope.Success && response.IsSuccessStatusCode,
            Data = envelope.Data,
            Message = envelope.Message ?? (envelope.Success ? string.Empty : "The request failed."),
            Errors = envelope.Errors,
            StatusCode = response.StatusCode,
            CorrelationId = envelope.CorrelationId,
        };
    }
}
