using Microsoft.Extensions.DependencyInjection;

namespace HotelPOS.Desktop.Services.Navigation;

/// <summary>Implemented by view-models that load data when they are shown.</summary>
public interface INavigationAware
{
    Task OnNavigatedToAsync(object? parameter);

    void OnNavigatedFrom();
}

/// <summary>Implemented by pages that can reload their data from the API (e.g. after a reconnect).</summary>
public interface IRefreshable
{
    Task RefreshAsync();
}

/// <summary>The window's top-level screens: configuration, login, password change, main shell.</summary>
public interface IAppNavigator
{
    Task ShowConfigurationAsync(bool isFirstRun);

    Task ShowLoginAsync(string? message = null);

    Task ShowChangePasswordAsync();

    Task ShowShellAsync();

    Task LogoutAsync(string? message = null);
}

/// <summary>Page navigation inside the shell (sidebar modules and sub-pages).</summary>
public interface INavigationService
{
    Task NavigateToAsync(string moduleKey, object? parameter = null);

    Task NavigateToPageAsync<TViewModel>(object? parameter = null)
        where TViewModel : class;

    Task GoHomeAsync();
}

/// <summary>The shell registers itself here so pages can navigate without referencing it.</summary>
public interface INavigationHost
{
    Task NavigateToAsync(string moduleKey, object? parameter);

    Task ShowPageAsync(object page, object? parameter);

    Task GoHomeAsync();
}

public sealed class NavigationService : INavigationService
{
    private readonly IServiceProvider _services;
    private INavigationHost? _host;

    public NavigationService(IServiceProvider services)
    {
        _services = services;
    }

    public void Attach(INavigationHost host) => _host = host;

    public void Detach(INavigationHost host)
    {
        if (ReferenceEquals(_host, host))
        {
            _host = null;
        }
    }

    public Task NavigateToAsync(string moduleKey, object? parameter = null) =>
        _host?.NavigateToAsync(moduleKey, parameter) ?? Task.CompletedTask;

    public Task NavigateToPageAsync<TViewModel>(object? parameter = null)
        where TViewModel : class =>
        _host?.ShowPageAsync(ActivatorUtilities.CreateInstance<TViewModel>(_services), parameter) ?? Task.CompletedTask;

    public Task GoHomeAsync() => _host?.GoHomeAsync() ?? Task.CompletedTask;
}
