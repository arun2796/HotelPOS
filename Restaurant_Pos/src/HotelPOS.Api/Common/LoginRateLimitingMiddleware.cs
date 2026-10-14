using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Common;
using Microsoft.Extensions.Options;

namespace HotelPOS.Api.Common;

public sealed class LoginRateLimitOptions
{
    public const string SectionName = "Security:RateLimit";

    public int MaxPerIp { get; set; } = 10;
    public int MaxPerUser { get; set; } = 5;
    public int WindowSeconds { get; set; } = 60;
}

public sealed class LoginRateLimiter
{
    // Above this many tracked keys, keys with no attempt inside the window are dropped, so spraying random usernames
    // cannot grow memory without bound.
    private const int PruneThreshold = 10_000;

    private readonly ConcurrentDictionary<string, AttemptWindow> _ipAttempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, AttemptWindow> _userAttempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly LoginRateLimitOptions _options;

    public LoginRateLimiter(IOptions<LoginRateLimitOptions> options)
    {
        _options = options.Value;
    }

    private TimeSpan Window => TimeSpan.FromSeconds(_options.WindowSeconds);

    public (bool Allowed, int RetryAfter) CheckAndRecord(string? ip, string? username, DateTime now)
    {
        var ipWindow = string.IsNullOrWhiteSpace(ip) ? null : Get(_ipAttempts, ip, now);
        var userWindow = string.IsNullOrWhiteSpace(username) ? null : Get(_userAttempts, username.Trim().ToUpperInvariant(), now);

        // Both windows are locked together (in a fixed order) so concurrent bursts cannot slip past the limit between
        // the check and the record.
        var first = ipWindow ?? userWindow;
        var second = ipWindow is null ? null : userWindow;
        if (first is null)
        {
            return (true, 0);
        }

        lock (first)
        {
            lock (second ?? first)
            {
                var ipRetry = ipWindow?.RetryAfter(_options.MaxPerIp, Window, now) ?? 0;
                var userRetry = userWindow?.RetryAfter(_options.MaxPerUser, Window, now) ?? 0;
                if (ipRetry > 0 || userRetry > 0)
                {
                    return (false, Math.Max(ipRetry, userRetry));
                }

                ipWindow?.Record(now);
                userWindow?.Record(now);
                return (true, 0);
            }
        }
    }

    public void Reset()
    {
        _ipAttempts.Clear();
        _userAttempts.Clear();
    }

    private AttemptWindow Get(ConcurrentDictionary<string, AttemptWindow> windows, string key, DateTime now)
    {
        if (windows.Count > PruneThreshold)
        {
            var cutoff = now - Window;
            foreach (var (staleKey, window) in windows)
            {
                if (window.IsIdleSince(cutoff))
                {
                    windows.TryRemove(staleKey, out _);
                }
            }
        }

        return windows.GetOrAdd(key, _ => new AttemptWindow());
    }

    // Callers lock the instance around RetryAfter + Record.
    private sealed class AttemptWindow
    {
        private readonly Queue<DateTime> _attempts = new();

        public int RetryAfter(int max, TimeSpan window, DateTime now)
        {
            while (_attempts.Count > 0 && _attempts.Peek() <= now - window)
            {
                _attempts.Dequeue();
            }

            return _attempts.Count < max
                ? 0
                : Math.Max(1, (int)Math.Ceiling((_attempts.Peek() + window - now).TotalSeconds));
        }

        public void Record(DateTime now) => _attempts.Enqueue(now);

        public bool IsIdleSince(DateTime cutoff)
        {
            lock (this)
            {
                return _attempts.Count == 0 || _attempts.Last() <= cutoff;
            }
        }
    }
}

public sealed class LoginRateLimitingMiddleware
{
    // Every anonymous endpoint that checks a password. The approval endpoint takes "approverUsername".
    private static readonly (PathString Path, string UsernameField)[] Guarded =
    {
        (new PathString("/api/auth/login"), "username"),
        (new PathString("/api/auth/approval"), "approverUsername"),
    };

    private readonly RequestDelegate _next;
    private readonly LoginRateLimiter _limiter;
    private readonly IClock _clock;

    public LoginRateLimitingMiddleware(RequestDelegate next, LoginRateLimiter limiter, IClock clock)
    {
        _next = next;
        _limiter = limiter;
        _clock = clock;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var usernameField = HttpMethods.IsPost(context.Request.Method) ? GuardedField(context.Request.Path) : null;
        if (usernameField is not null)
        {
            // The socket address, never a forwarded header: a client must not be able to pick its own bucket.
            var ip = context.Connection.RemoteIpAddress?.ToString();
            var username = await ReadUsernameAsync(context.Request, usernameField);
            var (allowed, retryAfter) = _limiter.CheckAndRecord(ip, username, _clock.UtcNow);
            if (!allowed)
            {
                await ApiResponseWriter.WriteErrorAsync(
                    context,
                    StatusCodes.Status429TooManyRequests,
                    ErrorCodes.RateLimited,
                    "Too many login attempts. Please try again later.",
                    configureHeaders: h => h["Retry-After"] = retryAfter.ToString(CultureInfo.InvariantCulture));
                return;
            }
        }

        await _next(context);
    }

    // Routing ignores case and a trailing slash, so the match must too or "/api/auth/login/" would skip the limit.
    private static string? GuardedField(PathString path)
    {
        var trimmed = new PathString(path.Value?.TrimEnd('/'));
        foreach (var (guardedPath, field) in Guarded)
        {
            if (trimmed.Equals(guardedPath, StringComparison.OrdinalIgnoreCase))
            {
                return field;
            }
        }

        return null;
    }

    private static async Task<string?> ReadUsernameAsync(HttpRequest request, string field)
    {
        request.EnableBuffering();
        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: request.HttpContext.RequestAborted);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.NameEquals(field) || string.Equals(property.Name, field, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                }
            }

            return null;
        }
        catch (JsonException)
        {
            // Malformed bodies are still counted per IP; model binding answers them with 400.
            return null;
        }
        catch (InvalidOperationException)
        {
            // The root is not a JSON object.
            return null;
        }
        finally
        {
            request.Body.Position = 0;
        }
    }
}
