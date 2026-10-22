using Microsoft.AspNetCore.StaticFiles;
using HotelPOS.Api.Common;
using HotelPOS.Api.Hubs;
using HotelPOS.Application;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Infrastructure;
using HotelPOS.Infrastructure.Media;
using HotelPOS.Infrastructure.Persistence;
using HotelPOS.Infrastructure.Updates;
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
// Phase 18: serve the referenced browser client's static web assets outside Development too (tests run as "Testing");
// after publish the assets are physical files under wwwroot/app and this call is a no-op.
builder.WebHost.UseStaticWebAssets();

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

// First hosted service = first to start: the database start-up runs before the other hosted services and before
// Kestrel listens, and after the Service Control Manager has been answered (see StartupHostedService).
builder.Services.AddHostedService<StartupHostedService>();
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddApi(builder.Configuration, builder.Environment);

var app = builder.Build();

// Phase 20.9: HSTS only when an HTTPS endpoint is configured (browsers ignore it on plain HTTP anyway). No
// UseHttpsRedirection: the Windows terminals keep working on plain port 5000.
// "Hsts:Enabled" lets the test host exercise this (tests run as "Testing", where HSTS is otherwise off).
var hstsAllowed = !app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing")
    || app.Configuration.GetValue<bool>("Hsts:Enabled");
if (hstsAllowed && app.Configuration.GetSection("Kestrel:Endpoints:Https").Exists())
{
    app.UseHsts();
}

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

// The terminal installer and latest.json for the auto-update. Anonymous (a terminal that is too old cannot sign in),
// not cacheable (latest.json must be fresh), and limited to the files publish.ps1 puts there.
var updatesFolder = app.Services.GetRequiredService<IOptions<UpdateOptions>>().Value.Folder;
Directory.CreateDirectory(updatesFolder);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(updatesFolder),
    // Phase 19: the Android staff app is downloaded from here; .apk is not in the default content-type map.
    ContentTypeProvider = new FileExtensionContentTypeProvider { Mappings = { [".apk"] = "application/vnd.android.package-archive" } },
    RequestPath = "/updates",
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
});

// Phase 18: the browser client lives under /app (framework files, index.html, css); API routes are untouched.
app.UseBlazorFrameworkFiles("/app");
app.UseStaticFiles();

app.UseMiddleware<LoginRateLimitingMiddleware>();
app.UseAuthentication();
app.UseMiddleware<RequestContextLoggingMiddleware>();
app.UseAuthorization();
app.UseMiddleware<AccessEnforcementMiddleware>();
app.UseMiddleware<LicenseEnforcementMiddleware>();
app.UseMiddleware<IdempotencyMiddleware>();

app.MapControllers();
app.MapHub<RestaurantHub>(HubRoutes.Restaurant);
app.MapHealthChecks("/health", ApiServiceRegistration.HealthCheckOptions).AllowAnonymous();
// Registered last and limited to /app/** so no API, hub, media or update route is ever shadowed.
app.MapFallbackToFile("/app/{*path:nonfile}", "app/index.html").AllowAnonymous();

try
{
    await app.RunAsync();
    return 0;
}
catch (Exception ex)
{
    // A failed start-up check or migration (already logged by StartupHostedService) or a host that could not start:
    // a non-zero exit lets the Windows service recovery settings restart the API once the cause is fixed.
    Console.Error.WriteLine($"HotelPOS API cannot start: {ex.Message}");
    return 1;
}

public partial class Program
{
}
