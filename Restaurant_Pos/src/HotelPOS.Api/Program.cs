using HotelPOS.Api.Common;
using HotelPOS.Api.Hubs;
using HotelPOS.Application;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Infrastructure;
using HotelPOS.Infrastructure.Persistence;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, logger) =>
{
    var logDirectory = context.Configuration["Logging:Directory"];
    if (string.IsNullOrWhiteSpace(logDirectory))
    {
        logDirectory = Path.Combine(context.HostingEnvironment.ContentRootPath, "logs");
    }

    const string template =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {CorrelationId} {Actor} {SourceContext}: {Message:lj}{NewLine}{Exception}";

    logger
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "HotelPOS.Api")
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            Path.Combine(logDirectory, "api-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            fileSizeLimitBytes: 50 * 1024 * 1024,
            rollOnFileSizeLimit: true,
            outputTemplate: template);
});

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddApi(builder.Configuration, builder.Environment);

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging(options =>
{
    // Health probes are frequent and uninteresting unless they fail.
    options.GetLevel = (context, _, exception) =>
        exception is not null || context.Response.StatusCode >= 500 ? LogEventLevel.Error
        : context.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose
        : LogEventLevel.Information;
});
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseStatusCodePages(async context =>
{
    var response = context.HttpContext.Response;
    if (!string.IsNullOrEmpty(response.ContentType))
    {
        return;
    }

    var (code, message) = response.StatusCode switch
    {
        StatusCodes.Status404NotFound => (ErrorCodes.NotFound, "The requested resource was not found."),
        StatusCodes.Status405MethodNotAllowed => (ErrorCodes.MethodNotAllowed, "This HTTP method is not allowed here."),
        StatusCodes.Status401Unauthorized => (ErrorCodes.Unauthenticated, "Authentication is required. Please log in."),
        StatusCodes.Status403Forbidden => (ErrorCodes.Forbidden, "You do not have permission to perform this action."),
        _ => (ErrorCodes.ServerError, $"The request failed with status {response.StatusCode}."),
    };
    await ApiResponseWriter.WriteErrorAsync(context.HttpContext, response.StatusCode, code, message);
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.DocumentTitle = "HotelPOS API");
}

app.UseAuthentication();
app.UseMiddleware<RequestContextLoggingMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapHub<RestaurantHub>(HubRoutes.Restaurant);
app.MapHealthChecks("/health", ApiServiceRegistration.HealthCheckOptions).AllowAnonymous();

await app.Services.InitializeDatabaseAsync();

app.Logger.LogInformation("HotelPOS API {Version} starting ({Environment})", ApiServiceRegistration.ApiVersion, app.Environment.EnvironmentName);
await app.RunAsync();

/// <summary>Entry point marker, used by integration tests (WebApplicationFactory).</summary>
public partial class Program
{
}
