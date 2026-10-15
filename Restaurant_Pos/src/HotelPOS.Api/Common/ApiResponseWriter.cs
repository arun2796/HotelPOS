using System.Text.Json;
using HotelPOS.Contracts.Common;

namespace HotelPOS.Api.Common;

public static class ApiResponseWriter
{
    public static async Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        string code,
        string message,
        IReadOnlyList<ApiError>? errors = null,
        Action<IHeaderDictionary>? configureHeaders = null)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        configureHeaders?.Invoke(context.Response.Headers);

        var body = ApiResponse.Fail(
            message,
            errors ?? new[] { new ApiError(code, message) },
            context.TraceIdentifier);

        await JsonSerializer.SerializeAsync(context.Response.Body, body, PosJson.Options, context.RequestAborted);
    }
}
