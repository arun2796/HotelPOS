using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace HotelPOS.Desktop.Tests.EndToEnd;

/// <summary>Skips unless HOTELPOS_E2E=1: these tests start the real API process and need PostgreSQL.</summary>
public sealed class E2EFactAttribute : FactAttribute
{
    public E2EFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("HOTELPOS_E2E") != "1")
        {
            Skip = "End-to-end test. Set HOTELPOS_E2E=1 (requires PostgreSQL and a built HotelPOS.Api).";
        }
    }
}

/// <summary>Runs the built HotelPOS.Api as a separate process so tests can kill and restart the server.</summary>
public sealed class ApiProcess : IAsyncDisposable
{
    private const string Database = "hotelpos_e2e";
    private readonly string _dll;
    private Process? _process;

    private ApiProcess(string dll, int port)
    {
        _dll = dll;
        Port = port;
    }

    public int Port { get; }

    public string BaseUrl => $"http://127.0.0.1:{Port}";

    public static async Task<ApiProcess> StartAsync()
    {
        var api = new ApiProcess(FindApiDll(), FreePort());
        await api.StartProcessAsync();
        return api;
    }

    public void Kill()
    {
        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(10_000);
        }

        _process?.Dispose();
        _process = null;
    }

    public Task RestartAsync()
    {
        Kill();
        return StartProcessAsync();
    }

    public ValueTask DisposeAsync()
    {
        Kill();
        return ValueTask.CompletedTask;
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string FindApiDll()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HotelPOS.sln")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("Could not locate HotelPOS.sln above the test output folder.");
        }

        var candidates = new[] { "Debug", "Release" }
            .Select(c => Path.Combine(dir.FullName, "src", "HotelPOS.Api", "bin", c, "net8.0", "HotelPOS.Api.dll"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();
        return candidates.FirstOrDefault()
            ?? throw new InvalidOperationException("HotelPOS.Api.dll not found. Build the solution first.");
    }

    private async Task StartProcessAsync()
    {
        var info = new ProcessStartInfo("dotnet", $"\"{_dll}\" --urls {BaseUrl}")
        {
            WorkingDirectory = Path.GetDirectoryName(_dll)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        info.Environment["ConnectionStrings__HotelPOS"] =
            $"{HotelPOS.Testing.TestPostgres.Server.TrimEnd(';')};Database={Database}";
        info.Environment["Logging__Directory"] = Path.Combine(Path.GetTempPath(), "hotelpos-e2e-logs");

        _process = Process.Start(info) ?? throw new InvalidOperationException("Could not start the API process.");
        _process.OutputDataReceived += (_, _) => { };
        _process.ErrorDataReceived += (_, _) => { };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException($"The API process exited with code {_process.ExitCode}.");
            }

            try
            {
                using var response = await http.GetAsync($"{BaseUrl}/health");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Not listening yet.
            }

            await Task.Delay(500);
        }

        throw new TimeoutException("The API did not become healthy within 90 seconds.");
    }
}
