using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Admin;
using HotelPOS.Desktop.Modules.Auth;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Desktop.Shell;

/// <summary>
/// The logged-in workspace: role-filtered sidebar, top bar, current page and status bar. One instance
/// per login; disposed at logout.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, INavigationAware, INavigationHost, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly IAuthSession _session;
    private readonly IRealtimeClient _realtime;
    private readonly IAppNavigator _navigator;
    private readonly NavigationService _navigation;
    private readonly ISystemApi _systemApi;
    private readonly ThemeService _theme;
    private readonly ILogger<ShellViewModel> _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _suppressNavigation;

    public ShellViewModel(
        IServiceProvider services,
        IAuthSession session,
        IRealtimeClient realtime,
        IAppNavigator navigator,
        NavigationService navigation,
        ISystemApi systemApi,
        ThemeService theme,
        IClientSettingsService settings,
        ILogger<ShellViewModel> logger)
    {
        _services = services;
        _session = session;
        _realtime = realtime;
        _navigator = navigator;
        _navigation = navigation;
        _systemApi = systemApi;
        _theme = theme;
        _logger = logger;
        StatusBar = new StatusBarViewModel(realtime, settings);
    }

    public ObservableCollection<NavItemViewModel> NavItems { get; } = new();

    public StatusBarViewModel StatusBar { get; }

    [ObservableProperty]
    private NavItemViewModel? _selectedNavItem;

    [ObservableProperty]
    private object? _currentPage;

    [ObservableProperty]
    private string _restaurantName = "HotelPOS";

    [ObservableProperty]
    private string _clock = string.Empty;

    public string UserDisplayName => _session.User?.DisplayName ?? string.Empty;

    public string RolesText => string.Join(", ", _session.User?.Roles ?? Array.Empty<string>());

    public string UserInitials => Initials(UserDisplayName);

    public bool IsDarkTheme => _theme.Current == ThemeService.Dark;

    public async Task OnNavigatedToAsync(object? parameter)
    {
        _navigation.Attach(this);
        _realtime.Reconnected += OnReconnected;

        BuildNavigation();
        UpdateClock();
        _ = RunClockAsync(_lifetime.Token);

        await _realtime.StartAsync();
        await GoHomeAsync();
        await LoadRestaurantNameAsync();
    }

    public void OnNavigatedFrom()
    {
        (CurrentPage as INavigationAware)?.OnNavigatedFrom();
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        _realtime.Reconnected -= OnReconnected;
        _navigation.Detach(this);
        StatusBar.Dispose();
        (CurrentPage as IDisposable)?.Dispose();
    }

    public async Task NavigateToAsync(string moduleKey, object? parameter)
    {
        var module = NavItems.FirstOrDefault(n => n.Key == moduleKey)?.Module;
        if (module is null)
        {
            _logger.LogWarning("Navigation to {Module} refused: not available for this user", moduleKey);
            return;
        }

        object page = module.ViewModelType is null
            ? new PlaceholderViewModel(module)
            : ActivatorUtilities.CreateInstance(_services, module.ViewModelType);

        SelectWithoutNavigating(module.Key);
        await ShowPageAsync(page, parameter);
    }

    public async Task ShowPageAsync(object page, object? parameter)
    {
        var previous = CurrentPage;
        (previous as INavigationAware)?.OnNavigatedFrom();
        (previous as IDisposable)?.Dispose();

        CurrentPage = page;
        if (page is INavigationAware aware)
        {
            try
            {
                await aware.OnNavigatedToAsync(parameter);
            }
            catch (Exception ex)
            {
                // A page that fails to load must not take the shell down.
                _logger.LogError(ex, "Page {Page} failed to load", page.GetType().Name);
            }
        }
    }

    public Task GoHomeAsync()
    {
        var roles = _session.User?.Roles ?? Array.Empty<string>();
        var home = ModuleRegistry.HomeFor(roles.ToList());
        return NavigateToAsync(NavItems.Any(n => n.Key == home) ? home : NavItems.FirstOrDefault()?.Key ?? home, null);
    }

    partial void OnSelectedNavItemChanged(NavItemViewModel? value)
    {
        if (value is not null && !_suppressNavigation)
        {
            _ = NavigateToAsync(value.Key, null);
        }
    }

    [RelayCommand]
    private Task LogoutAsync() => _navigator.LogoutAsync();

    [RelayCommand]
    private async Task ChangePasswordAsync()
    {
        SelectWithoutNavigating(null);
        await ShowPageAsync(ActivatorUtilities.CreateInstance<ChangePasswordViewModel>(_services), ChangePasswordMode.Voluntary);
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        _theme.Toggle();
        OnPropertyChanged(nameof(IsDarkTheme));
    }

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => char.IsLetter(p[0]))
            .ToList();
        return parts.Count switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[1][..1]).ToUpperInvariant(),
        };
    }

    private void BuildNavigation()
    {
        NavItems.Clear();
        var roles = _session.User?.Roles ?? Array.Empty<string>();
        string? group = null;
        foreach (var module in ModuleRegistry.ForRoles(roles.ToList()))
        {
            NavItems.Add(new NavItemViewModel(module, showGroupHeader: module.Group != group));
            group = module.Group;
        }

        OnPropertyChanged(nameof(UserDisplayName));
        OnPropertyChanged(nameof(RolesText));
        OnPropertyChanged(nameof(UserInitials));
    }

    private void SelectWithoutNavigating(string? key)
    {
        _suppressNavigation = true;
        try
        {
            SelectedNavItem = key is null ? null : NavItems.FirstOrDefault(n => n.Key == key);
        }
        finally
        {
            _suppressNavigation = false;
        }
    }

    private async Task LoadRestaurantNameAsync()
    {
        var result = await _systemApi.GetPublicSettingsAsync(_lifetime.Token);
        var name = result.Data?.FirstOrDefault(s => s.Key == SettingKeys.RestaurantName)?.Value;
        if (!string.IsNullOrWhiteSpace(name))
        {
            RestaurantName = name;
        }
    }

    private void OnReconnected(object? sender, EventArgs e)
    {
        _logger.LogInformation("Reconnected: refreshing {Page} from the server", CurrentPage?.GetType().Name);
        _ = LoadRestaurantNameAsync();
        if (CurrentPage is IRefreshable refreshable)
        {
            _ = refreshable.RefreshAsync();
        }
    }

    private async Task RunClockAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                UpdateClock();
            }
        }
        catch (OperationCanceledException)
        {
            // Shell closed.
        }
    }

    private void UpdateClock() => Clock = DateTime.Now.ToString("ddd d MMM · HH:mm", CultureInfo.CurrentCulture);
}
