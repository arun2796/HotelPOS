using FluentValidation;
using HotelPOS.Application.Auth;
using HotelPOS.Application.Devices;
using HotelPOS.Application.Settings;
using HotelPOS.Application.Users;
using Microsoft.Extensions.DependencyInjection;

namespace HotelPOS.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IDeviceService, DeviceService>();
        services.AddScoped<SettingsService>();
        services.AddScoped<ISettingsService>(sp => sp.GetRequiredService<SettingsService>());
        services.AddScoped<ISystemInfoService>(sp => sp.GetRequiredService<SettingsService>());

        return services;
    }
}
