using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HotelPOS.Client.Services;

public static class ClientServiceCollectionExtensions
{
    /// <summary>
    /// Everything the shared pages need. The host registers, before calling this: an <see cref="HttpClient"/>,
    /// <see cref="IClientHost"/>, <see cref="ISessionTransport"/> and <see cref="IBrowserStorage"/>.
    /// </summary>
    public static IServiceCollection AddHotelPosClient(this IServiceCollection services)
    {
        services.AddAuthorizationCore();
        services.AddScoped<DeviceIdentity>();
        services.AddScoped<WebAuthState>();
        services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<WebAuthState>());
        services.AddScoped<WebApiClient>();
        services.AddScoped<WaiterApi>();
        services.AddScoped<KitchenApi>();
        services.AddScoped<IdempotentSender>();
        services.AddScoped<DraftStore>();
        services.AddScoped<FavouritesStore>();
        services.AddScoped<WebRealtimeClient>();
        services.AddScoped<ToastService>();
        services.AddScoped<NotificationCenter>();
        services.AddScoped<NotificationPreferences>();
        services.TryAddSingleton<IAlertSignal, SilentAlertSignal>();
        services.AddScoped<NotificationRouter>();
        services.AddScoped<ShellState>();
        return services;
    }
}
