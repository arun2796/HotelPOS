using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HotelPOS.Contracts.Common;

namespace HotelPOS.Client.Services;

/// <summary>
/// Thin HTTP client over the existing API: absolute URLs from <see cref="IClientHost"/>, bearer token from
/// <see cref="WebAuthState"/>, envelope unwrapping, the Idempotency-Key header on writes, and one silent
/// refresh-and-retry on 401 (the same behaviour as the desktop's AuthDelegatingHandler).
/// </summary>
public sealed class WebApiClient
{
    private readonly HttpClient _http;
    private readonly IClientHost _host;
    private readonly WebAuthState _auth;

    public WebApiClient(HttpClient http, IClientHost host, WebAuthState auth)
    {
        _http = http;
        _host = host;
        _auth = auth;
    }

    public Task<ApiResult<T>> GetAsync<T>(string path, CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Get, path, null, null, cancellationToken);

    public Task<ApiResult<T>> PostAsync<T>(string path, object? body, Guid? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Post, path, body, idempotencyKey, cancellationToken);

    public Task<ApiResult<T>> PutAsync<T>(string path, object? body, Guid? idempotencyKey = null, CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Put, path, body, idempotencyKey, cancellationToken);

    public Task<ApiResult<T>> DeleteAsync<T>(string path, CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Delete, path, null, null, cancellationToken);

    /// <summary>A file answer (a PDF) rather than an envelope. Same bearer token and refresh-and-retry as every other call; an error comes back as the usual envelope.</summary>
    public async Task<ApiResult<byte[]>> GetBytesAsync(string path, CancellationToken cancellationToken = default)
    {
        var result = await GetBytesOnceAsync(path, cancellationToken);
        if (result.StatusCode == HttpStatusCode.Unauthorized && await _auth.RefreshAsync())
        {
            result = await GetBytesOnceAsync(path, cancellationToken);
        }

        if (result.StatusCode == HttpStatusCode.Unauthorized)
        {
            await _auth.SignOutLocallyAsync("Your session has ended. Sign in again.");
        }

        return result;
    }

    private async Task<ApiResult<byte[]>> GetBytesOnceAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _host.Resolve(path));
        var token = await _auth.GetAccessTokenAsync();
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ApiResult<byte[]>.Fail(ErrorCodes.ConnectionUnavailable, "The server cannot be reached. Check the Wi-Fi and try again.");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "application/pdf")
            {
                return ApiResult<byte[]>.Ok(await response.Content.ReadAsByteArrayAsync(cancellationToken), response.StatusCode);
            }

            var failed = await ReadAsync<object>(response, cancellationToken);
            return failed.Success
                ? ApiResult<byte[]>.Fail(ErrorCodes.ServerError, "The server did not send a PDF.", response.StatusCode)
                : ApiResult<byte[]>.Fail(failed.ErrorCode, failed.Message, failed.StatusCode, failed.Errors);
        }
    }

    public async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string path, object? body, Guid? idempotencyKey, CancellationToken cancellationToken)
    {
        var result = await SendOnceAsync<T>(method, path, body, idempotencyKey, cancellationToken);
        if (result.StatusCode == HttpStatusCode.Unauthorized && await _auth.RefreshAsync())
        {
            result = await SendOnceAsync<T>(method, path, body, idempotencyKey, cancellationToken);
        }

        if (result.StatusCode == HttpStatusCode.Unauthorized)
        {
            await _auth.SignOutLocallyAsync("Your session has ended. Sign in again.");
        }

        return result;
    }

    private async Task<ApiResult<T>> SendOnceAsync<T>(HttpMethod method, string path, object? body, Guid? idempotencyKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, _host.Resolve(path));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: PosJson.Options);
        }

        if (idempotencyKey is { } key)
        {
            request.Headers.Add(PosHeaders.IdempotencyKey, key.ToString());
        }

        var token = await _auth.GetAccessTokenAsync();
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ApiResult<T>.Fail(ErrorCodes.ConnectionUnavailable, "The server cannot be reached. Check the Wi-Fi and try again.");
        }

        using (response)
        {
            return await ReadAsync<T>(response, cancellationToken);
        }
    }

    public static async Task<ApiResult<T>> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var replayed = response.Headers.Contains("Idempotent-Replayed");
        if (response.Content.Headers.ContentLength == 0 || response.StatusCode == HttpStatusCode.NoContent)
        {
            return response.IsSuccessStatusCode
                ? ApiResult<T>.Ok(default, response.StatusCode, replayed)
                : ApiResult<T>.Fail(ErrorCodes.ServerError, $"The server answered {(int)response.StatusCode}.", response.StatusCode);
        }

        ApiResponse<T>? envelope;
        try
        {
            envelope = await response.Content.ReadFromJsonAsync<ApiResponse<T>>(PosJson.Options, cancellationToken);
        }
        catch (JsonException)
        {
            envelope = null;
        }

        if (envelope is null)
        {
            return response.IsSuccessStatusCode
                ? ApiResult<T>.Ok(default, response.StatusCode, replayed)
                : ApiResult<T>.Fail(ErrorCodes.ServerError, $"Unexpected answer from the server ({(int)response.StatusCode}).", response.StatusCode);
        }

        if (envelope.Success)
        {
            return ApiResult<T>.Ok(envelope.Data, response.StatusCode, replayed);
        }

        var first = envelope.Errors.FirstOrDefault();
        return ApiResult<T>.Fail(first?.Code ?? ErrorCodes.ServerError, envelope.Message ?? first?.Message ?? "The request failed.", response.StatusCode, envelope.Errors);
    }
}
