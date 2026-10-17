using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Admin;
using HotelPOS.Desktop.Modules.Admin;
using HotelPOS.Desktop.Modules.Auth;
using HotelPOS.Desktop.Modules.Cash;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Auth;
using HotelPOS.Desktop.Services.Configuration;
using HotelPOS.Contracts.Security;
using HotelPOS.Desktop.Services.Menu;
using HotelPOS.Desktop.Services.Orders;
using HotelPOS.Desktop.Services.Printing;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Desktop.Shell;

public sealed partial class ShellViewModel : ObservableObject, INavigationAware, INavigationHost, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly IAuthSession _session;
    private readonly IRealtimeClient _realtime;
    private readonly IMenuCache _menuCache;
    private readonly INotificationRouter _notificationRouter;
    private readonly IKotAutoPrinter _kotPrinter;
    private readonly IAppNavigator _navigator;
    private readonly NavigationService _navigation;
    private readonly ISystemApi _systemApi;
    private readonly ThemeService _theme;
    private readonly IClientSettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly ILogger<ShellViewModel> _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _suppressNavigation;
    private ReconnectCoordinator? _reconnect;

    public ShellViewModel(
        IServiceProvider services,
        IAuthSession session,
        IRealtimeClient realtime,
        IMenuCache menuCache,
        INotificationRouter notificationRouter,
        IKotAutoPrinter kotPrinter,
        IPrintQueue printQueue,
        IAppNavigator navigator,
        NavigationService navigation,
        ISystemApi systemApi,
        ThemeService theme,
        IClientSettingsService settings,
        IDialogService dialogs,
        NoticeCenter notices,
        ILogger<ShellViewModel> logger)
    {
        _services = services;
        _session = session;
        _realtime = realtime;
        _menuCache = menuCache;
        _notificationRouter = notificationRouter;
        _kotPrinter = kotPrinter;
        _navigator = navigator;
        _navigation = navigation;
        _systemApi = systemApi;
        _theme = theme;
        _settings = settings;
        _dialogs = dialogs;
        _logger = logger;
        Notices = notices;
        StatusBar = new StatusBarViewModel(realtime, settings, printQueue);
        Palette = ActivatorUtilities.CreateInstance<CommandPaletteViewModel>(services);
        ShiftBar = services.GetService<ShiftBarViewModel>() ?? ActivatorUtilities.CreateInstance<ShiftBarViewModel>(services);
        Banner = services.GetService<StatusBannerViewModel>() ?? ActivatorUtilities.CreateInstance<StatusBannerViewModel>(services);
        _isSidebarCollapsed = settings.Current.SidebarCollapsed;
        StatusBar.PropertyChanged += OnStatusBarChanged;
    }

    private void OnStatusBarChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StatusBarViewModel.FailedPrintCount))
        {
            UpdateBadges();
        }
    }

    public ObservableCollection<NavItemViewModel> NavItems { get; } = new();

    public StatusBarViewModel StatusBar { get; }

    public CommandPaletteViewModel Palette { get; }

    public ShiftBarViewModel ShiftBar { get; }

    // Licence and update notes above the page.
    public StatusBannerViewModel Banner { get; }

    public NoticeCenter Notices { get; }

    [ObservableProperty]
    private bool _isSidebarCollapsed;

    [ObservableProperty]
    private bool _isNoticesOpen;

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
        _reconnect = new ReconnectCoordinator(_realtime, Notices, _services.GetRequiredService<IUiDispatcher>(), () => CurrentPage, LoadRestaurantNameAsync);

        BuildNavigation();
        UpdateClock();
        _ = RunClockAsync(_lifetime.Token);

        await _realtime.StartAsync();

        // Every role works with the menu (ordering, kitchen sold-out, billing); load it in the background.
        _ = _menuCache.StartAsync();
        // Bell entries and toasts for this user's role (replaces the waiter-only "order ready" notifier).
        _notificationRouter.Start();

        _kotPrinter.Start();

        await ShiftBar.StartAsync();
        _ = Banner.RefreshAsync();
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
        _reconnect?.Dispose();
        _menuCache.Stop();
        _notificationRouter.Stop();
        _kotPrinter.Stop();
        _navigation.Detach(this);
        StatusBar.Dispose();
        ShiftBar.Stop();
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
        // A module may be a deep link into a shared page (Kitchen stations → the Menu hub's Stations tab).
        await ShowPageAsync(page, parameter ?? module.DefaultParameter);
    }

    public async Task ShowPageAsync(object page, object? parameter)
    {
        var previous = CurrentPage;
        (previous as INavigationAware)?.OnNavigatedFrom();
        (previous as IDisposable)?.Dispose();
        _services.GetService<INotificationService>()?.DismissStale();

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
        OnPropertyChanged(nameof(PageDescription));
        if (value is not null && !_suppressNavigation)
        {
            _ = NavigateToAsync(value.Key, null);
        }
    }

    [RelayCommand]
    private Task LogoutAsync() => _navigator.LogoutAsync();

    public string PageDescription => SelectedNavItem?.Module.Description ?? string.Empty;

    private async Task BackUpNowAsync()
    {
        await NavigateToAsync(ModuleRegistry.Backup, null);
        if (CurrentPage is BackupViewModel backup && backup.BackupNowCommand.CanExecute(null))
        {
            await backup.BackupNowCommand.ExecuteAsync(null);
        }
    }

    /// <summary>The "Notifications…" dialog while open; null otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPreferencesOpen))]
    private NotificationPreferencesViewModel? _preferences;

    public bool IsPreferencesOpen => Preferences is not null;

    [RelayCommand]
    private void OpenNotificationPreferences()
    {
        IsNoticesOpen = false;
        Preferences = new NotificationPreferencesViewModel(_services.GetRequiredService<NotificationPreferences>(), () => Preferences = null);
    }

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

    /// <summary>Below this window width the sidebar collapses on its own (the cashier's three panes need the room at 1366 px).</summary>
    public const double AutoCollapseBelowWidth = 1440;

    public static bool IsNarrowWindow(double width) => width > 0 && width < AutoCollapseBelowWidth;

    private bool? _narrowWindow;

    /// <summary>
    /// Called by the shell view when the window width changes. Crossing the threshold collapses the sidebar, or restores
    /// the remembered choice when the window is wide again; the user can still expand it by hand (ToggleSidebar).
    /// </summary>
    public void ApplyWindowWidth(double width)
    {
        if (width <= 0)
        {
            return;
        }

        var narrow = IsNarrowWindow(width);
        if (_narrowWindow == narrow)
        {
            return;
        }

        _narrowWindow = narrow;
        IsSidebarCollapsed = narrow || _settings.Current.SidebarCollapsed;
    }

    private void UpdateBadges()
    {
        var printLog = NavItems.FirstOrDefault(n => n.Key == ModuleRegistry.PrintLog);
        if (printLog is not null)
        {
            printLog.Badge = StatusBar.FailedPrintCount;
        }
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarCollapsed = !IsSidebarCollapsed;
        var settings = _settings.Current;
        settings.SidebarCollapsed = IsSidebarCollapsed;
        _settings.Save(settings);
    }

    [RelayCommand]
    private Task OpenPaletteAsync()
    {
        IsNoticesOpen = false;
        return Palette.OpenAsync();
    }

    [RelayCommand]
    private void ToggleNotices()
    {
        IsNoticesOpen = !IsNoticesOpen;
    }

    [RelayCommand]
    private void MarkAllNoticesRead() => Notices.MarkAllRead();

    [RelayCommand]
    private void AcknowledgeNotice(NoticeItem? notice)
    {
        if (notice is not null)
        {
            Notices.Acknowledge(notice.Id);
        }
    }

    // Tapping a notice marks it read and opens the screen it is about.
    [RelayCommand]
    private async Task OpenNoticeAsync(NoticeItem? notice)
    {
        if (notice is null)
        {
            return;
        }

        Notices.MarkRead(notice.Id);
        if (notice.Target is { } target)
        {
            IsNoticesOpen = false;
            await _notificationRouter.OpenAsync(target);
        }
    }

    [RelayCommand]
    private Task ShowHelpAsync() => _dialogs.AlertAsync("Keyboard shortcuts", string.Join(Environment.NewLine, new[]
    {
        "Ctrl+K   Search screens, tables, orders and bills",
        "Ctrl+D   Dashboard        Ctrl+T   Tables",
        "Ctrl+B   Billing          Ctrl+R   Reports",
        "F2 / F3 / F4 / F9   Cash, card, UPI, split (bill screen)",
        "F11   Full screen        Esc   Close the open panel",
    }));

    // Window-level keys (forwarded by the shell view). Returns true when the key was used.
    public bool HandleShortcut(Key key, ModifierKeys modifiers)
    {
        if (modifiers == ModifierKeys.Control && key == Key.K)
        {
            if (Palette.IsOpen)
            {
                Palette.CloseCommand.Execute(null);
            }
            else
            {
                _ = OpenPaletteAsync();
            }

            return true;
        }

        if (key == Key.Escape && Preferences is not null)
        {
            Preferences = null;
            return true;
        }

        if (key == Key.Escape && (Palette.IsOpen || IsNoticesOpen))
        {
            Palette.CloseCommand.Execute(null);
            IsNoticesOpen = false;
            return true;
        }

        if (modifiers == ModifierKeys.None && key == Key.F1)
        {
            _ = ShowHelpAsync();
            return true;
        }

        var module = modifiers != ModifierKeys.Control ? null : key switch
        {
            Key.D => ModuleRegistry.Dashboard,
            Key.T => ModuleRegistry.Tables,
            Key.B => ModuleRegistry.Billing,
            Key.R => ModuleRegistry.Reports,
            _ => null,
        };
        if (module is null || NavItems.All(n => n.Key != module))
        {
            return false;
        }

        _ = NavigateToAsync(module, null);
        return true;
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
        var permissions = _session.User?.Permissions ?? Array.Empty<string>();
        var roleList = roles.ToList();
        var permissionList = permissions.ToList();

        // Visible entries by group (empty groups never appear), then the modules that stay reachable without a sidebar entry
        // (Customers): they remain in NavItems so navigation, the palette and "Find" keep working; the sidebar hides them.
        var firstGroup = true;
        foreach (var (_, modules) in ModuleRegistry.NavigationFor(roleList, permissionList))
        {
            for (var i = 0; i < modules.Count; i++)
            {
                NavItems.Add(new NavItemViewModel(modules[i], showGroupHeader: i == 0, showGroupDivider: i == 0 && !firstGroup));
            }

            firstGroup = false;
        }

        foreach (var module in ModuleRegistry.ForUser(roleList, permissionList).Where(m => !m.ShowInNav))
        {
            NavItems.Add(new NavItemViewModel(module, showGroupHeader: false));
        }

        UpdateBadges();

        var allowed = NavItems.Select(n => n.Key).ToHashSet();
        var actions = new List<PaletteEntry>
        {
            new("Switch light / dark theme", "Remembered on this terminal", "", CommandPaletteViewModel.ActionGroup,
                () => { ToggleTheme(); return Task.CompletedTask; }, "dark mode theme"),
            new("Change my password", "Choose a new password", "", CommandPaletteViewModel.ActionGroup,
                ChangePasswordAsync, "password"),
            new("Keyboard shortcuts", "All shortcut keys", "", CommandPaletteViewModel.ActionGroup,
                ShowHelpAsync, "help keys", "F1"),
            new("Sign out", "End your session on this terminal", "", CommandPaletteViewModel.ActionGroup,
                LogoutAsync, "logout log out exit"),
        };
        if (allowed.Contains(ModuleRegistry.Tables))
        {
            actions.Insert(0, new PaletteEntry("New order", "Pick a table on the floor map", "", CommandPaletteViewModel.ActionGroup,
                () => NavigateToAsync(ModuleRegistry.Tables, null), "order table occupy"));
        }

        if (allowed.Contains(ModuleRegistry.Backup))
        {
            actions.Insert(0, new PaletteEntry("Back up now", "Start a database backup", "", CommandPaletteViewModel.ActionGroup,
                BackUpNowAsync, "backup database"));
        }

        Palette.Configure(NavItems.Select(n => n.Module), actions);

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
