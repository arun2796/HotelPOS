using HotelPOS.Api.Common;
using HotelPOS.Api.Hubs;
using HotelPOS.Application;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Infrastructure;
using HotelPOS.Infrastructure.Media;
using HotelPOS.Infrastructure.Persistence;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // As a Windows Service the working directory is System32; content (appsettings, media) lives next to the exe.
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : default,
});

// No-op when started from a console or by the tests; under the Service Control Manager it reports start/stop.
builder.Host.UseWindowsService(options => options.ServiceName = "HotelPOS API");

builder.Host.UseSerilog((context, services, logger) =>
{
    var logDirectory = ServerPaths.LogDirectory(context.Configuration, context.HostingEnvironment);

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
},
// The host owns its logger instead of the static Log.Logger, so hosts sharing one process (the API tests) keep
// their logs apart; disposing the app flushes it.
preserveStaticLogger: true);

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

// Menu pictures: anonymous and cacheable for a year, because every upload gets a new file name.
var menuImages = app.Services.GetRequiredService<IOptions<MediaOptions>>().Value.MenuImagesPath;
Directory.CreateDirectory(menuImages);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(menuImages),
    RequestPath = MenuImageRoutes.RequestPath,
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable",
});

app.UseMiddleware<LoginRateLimitingMiddleware>();
app.UseAuthentication();
app.UseMiddleware<RequestContextLoggingMiddleware>();
app.UseAuthorization();
app.UseMiddleware<DeviceEnforcementMiddleware>();
app.UseMiddleware<IdempotencyMiddleware>();

app.MapControllers();
app.MapHub<RestaurantHub>(HubRoutes.Restaurant);
app.MapHealthChecks("/health", ApiServiceRegistration.HealthCheckOptions).AllowAnonymous();

try
{
    await app.Services.InitializeDatabaseAsync();
}
catch (StartupCheckException ex)
{
    // A non-zero exit lets the Windows service recovery settings restart the API once the cause is fixed.
    app.Logger.LogCritical(ex, "HotelPOS API cannot start: {Reason}", ex.Message);
    await app.DisposeAsync();
    return 1;
}

StartupDiagnostics.WarnAboutLowDiskSpace(app.Services, app.Logger);
app.Logger.LogInformation("HotelPOS API {Version} starting ({Environment})", ApiServiceRegistration.ApiVersion, app.Environment.EnvironmentName);
await app.RunAsync();
return 0;

public partial class Program
{
}
