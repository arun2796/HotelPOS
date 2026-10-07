using Microsoft.Extensions.DependencyInjection;

namespace HotelPOS.Desktop.Services.Navigation;

public interface INavigationAware
{
    Task OnNavigatedToAsync(object? parameter);

    void OnNavigatedFrom();
}

public interface IRefreshable
{
    Task RefreshAsync();
}

public interface IAppNavigator
{
    Task ShowConfigurationAsync(bool isFirstRun);

    Task ShowLoginAsync(string? message = null);

    Task ShowChangePasswordAsync();

    Task ShowShellAsync();

    Task LogoutAsync(string? message = null);
}

public interface INavigationService
{
    Task NavigateToAsync(string moduleKey, object? parameter = null);

    Task NavigateToPageAsync<TViewModel>(object? parameter = null)
        where TViewModel : class;

    Task GoHomeAsync();
}

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
