using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using HotelPOS.Contracts.Menu;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Desktop.Services.Realtime;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Desktop.Services.Menu;

public interface IMenuCache
{
    MenuDto? Menu { get; }

    event EventHandler? Changed;

    Task StartAsync();

    void Stop();

    Task<bool> RefreshAsync();

    Task<string?> GetImageFileAsync(string? imageUrl);
}

public sealed class MenuCache : IMenuCache
{
    public const string ImageHttpClientName = "HotelPOS.Images";

    private readonly IMenuApi _menuApi;
    private readonly IRealtimeClient _realtime;
    private readonly IClientSettingsService _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AppPaths _paths;
    private readonly ILogger<MenuCache> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly ConcurrentDictionary<string, Task<string?>> _downloads = new(StringComparer.OrdinalIgnoreCase);
    private IDisposable? _subscription;

    public MenuCache(
        IMenuApi menuApi,
        IRealtimeClient realtime,
        IClientSettingsService settings,
        IHttpClientFactory httpClientFactory,
        AppPaths paths,
        ILogger<MenuCache> logger)
    {
        _menuApi = menuApi;
        _realtime = realtime;
        _settings = settings;
        _httpClientFactory = httpClientFactory;
        _paths = paths;
        _logger = logger;
    }

    public event EventHandler? Changed;

    public MenuDto? Menu { get; private set; }

    public async Task StartAsync()
    {
        if (_subscription is not null)
        {
            return;
        }

        _subscription = _realtime.Subscribe<MenuChangedEvent>(HubEvents.MenuChanged, OnMenuChanged);
        _realtime.Reconnected += OnReconnected;
        await RefreshAsync();
    }

    public void Stop()
    {
        _subscription?.Dispose();
        _subscription = null;
        _realtime.Reconnected -= OnReconnected;
        Menu = null;
    }

    public async Task<bool> RefreshAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            var result = await _menuApi.GetMenuAsync(Menu?.Version);
            if (!result.Success || result.Data is null)
            {
                _logger.LogWarning("Menu reload failed ({Error}); keeping version {Version}", result.Message, Menu?.Version);
                return false;
            }

            if (result.Data.NotModified)
            {
                return true;
            }

            Menu = result.Data;
            _logger.LogInformation("Menu version {Version} loaded: {Items} items", Menu.Version, Menu.Items.Count);
        }
        finally
        {
            _refreshGate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public Task<string?> GetImageFileAsync(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return Task.FromResult<string?>(null);
        }

        var fileName = Path.GetFileName(imageUrl);
        var localPath = Path.Combine(_paths.ImageCacheDirectory, fileName);
        if (File.Exists(localPath))
        {
            return Task.FromResult<string?>(localPath);
        }

        // One download per picture, however many tiles ask for it; a failed download can be retried later.
        var download = _downloads.GetOrAdd(fileName, _ => DownloadAsync(imageUrl, localPath));
        _ = download.ContinueWith(t => { if (t.Result is null) { _downloads.TryRemove(fileName, out _); } }, TaskScheduler.Default);
        return download;
    }

    private async Task<string?> DownloadAsync(string imageUrl, string localPath)
    {
        try
        {
            var baseUri = new Uri(_settings.Current.ApiBaseUrl.TrimEnd('/') + "/");
            var client = _httpClientFactory.CreateClient(ImageHttpClientName);
            var bytes = await client.GetByteArrayAsync(new Uri(baseUri, imageUrl.TrimStart('/'))).ConfigureAwait(false);

            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
            var temp = localPath + ".part";
            await File.WriteAllBytesAsync(temp, bytes).ConfigureAwait(false);
            File.Move(temp, localPath, overwrite: true);
            return localPath;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UriFormatException or TaskCanceledException)
        {
            _logger.LogWarning("Could not download menu picture {Url}: {Error}", imageUrl, ex.Message);
            return null;
        }
    }

    private void OnMenuChanged(MenuChangedEvent change)
    {
        if (change.MenuVersion != Menu?.Version)
        {
            _ = RefreshAsync();
        }
    }

    private void OnReconnected(object? sender, EventArgs e) => _ = RefreshAsync();
}
