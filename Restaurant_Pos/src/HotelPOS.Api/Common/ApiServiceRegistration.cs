using System.Reflection;
using System.Text.Json;
using HotelPOS.Api.Hubs;
using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Security;
using HotelPOS.Contracts.Common;
using HotelPOS.Infrastructure.Identity;
using HotelPOS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

namespace HotelPOS.Api.Common;

public static class ApiServiceRegistration
{
    public const int MinimumSigningKeyLength = 32;

    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        services.AddOptions<AppOptions>()
            .Bind(configuration.GetSection(AppOptions.SectionName))
            .PostConfigure(o => o.ApiVersion = ApiVersion);
        services.AddOptions<JwtOptions>()
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.SigningKey) && o.SigningKey.Length >= MinimumSigningKeyLength,
                $"Jwt:SigningKey must be configured and at least {MinimumSigningKeyLength} characters long.")
            .Validate(o => o.AccessTokenMinutes > 0 && o.RefreshTokenHours > 0, "Jwt token lifetimes must be positive.")
            .ValidateOnStart();

        services.AddControllers(options => options.Filters.Add<ValidationFilter>())
            .AddJsonOptions(options => PosJson.Apply(options.JsonSerializerOptions))
            .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = InvalidModelState);

        AddAuthentication(services);

        services.AddSignalR(options =>
            {
                options.EnableDetailedErrors = environment.IsDevelopment();
                options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
            })
            .AddJsonProtocol(options => PosJson.Apply(options.PayloadSerializerOptions));
        services.AddSingleton<ConnectionTracker>();
        services.AddSingleton<IRealtimeNotifier, SignalRRealtimeNotifier>();

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

        if (environment.IsDevelopment())
        {
            AddSwagger(services);
        }

        return services;
    }

    public static string ApiVersion { get; } =
        (typeof(ApiServiceRegistration).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0")
        .Split('+')[0];

    public static Task WriteHealthResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var body = new ApiResponse<object>
        {
            Success = report.Status == HealthStatus.Healthy,
            Data = new
            {
                Status = report.Status.ToString(),
                Checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString()),
                ServerTimeUtc = DateTime.UtcNow,
                ApiVersion,
            },
            CorrelationId = context.TraceIdentifier,
        };
        return JsonSerializer.SerializeAsync(context.Response.Body, body, PosJson.Options, context.RequestAborted);
    }

    public static HealthCheckOptions HealthCheckOptions { get; } = new()
    {
        ResponseWriter = WriteHealthResponse,
        ResultStatusCodes =
        {
            [HealthStatus.Healthy] = StatusCodes.Status200OK,
            [HealthStatus.Degraded] = StatusCodes.Status200OK,
            [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
        },
    };

    private static void AddAuthentication(IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Configured lazily so the values come from the final configuration (including test overrides).
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((options, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = JwtTokenService.CreateSigningKey(jwt.SigningKey),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = PosClaimTypes.Name,
                    RoleClaimType = PosClaimTypes.Role,
                };
                options.Events = new JwtBearerEvents
                {
                    // Browsers and the SignalR client cannot set headers on WebSocket requests.
                    OnMessageReceived = context =>
                    {
                        var token = context.Request.Query["access_token"].ToString();
                        if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                        {
                            context.Token = token;
                        }

                        return Task.CompletedTask;
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        await ApiResponseWriter.WriteErrorAsync(context.HttpContext, StatusCodes.Status401Unauthorized,
                            ErrorCodes.Unauthenticated, "Authentication is required. Please log in.");
                    },
                    OnForbidden = context => ApiResponseWriter.WriteErrorAsync(context.HttpContext, StatusCodes.Status403Forbidden,
                        ErrorCodes.Forbidden, "You do not have permission to perform this action."),
                };
            });

        services.AddAuthorization(options =>
        {
            // Secure by default: an endpoint without [Authorize]/[AllowAnonymous] still requires a login.
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        });
    }

    private static IActionResult InvalidModelState(ActionContext context)
    {
        var errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .SelectMany(entry => entry.Value!.Errors.Select(e => new ApiError(
                ErrorCodes.ValidationError,
                string.IsNullOrWhiteSpace(e.ErrorMessage) ? "The value is invalid." : e.ErrorMessage,
                string.IsNullOrEmpty(entry.Key) ? null : entry.Key.TrimStart('$', '.').ToCamelCase())))
            .ToList();

        return new BadRequestObjectResult(ApiResponse.Fail("The request is invalid.", errors, context.HttpContext.TraceIdentifier));
    }

    private static void AddSwagger(IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "HotelPOS API", Version = ApiVersion });
            var scheme = new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Paste the access token returned by POST /api/auth/login.",
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
            };
            options.AddSecurityDefinition("Bearer", scheme);
            options.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = Array.Empty<string>() });
            options.CustomSchemaIds(type => type.FullName?.Replace('+', '.'));
        });
    }
}
