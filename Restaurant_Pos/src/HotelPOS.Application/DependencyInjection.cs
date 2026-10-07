using FluentValidation;
using HotelPOS.Application.Auth;
using HotelPOS.Application.Devices;
using HotelPOS.Application.Floor;
using HotelPOS.Application.Kitchen;
using HotelPOS.Application.Menu;
using HotelPOS.Application.Orders;
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
        services.AddScoped<ISectionService, SectionService>();
        services.AddScoped<TableEvents>();
        services.AddScoped<ITableService, TableService>();
        services.AddScoped<OrderItemFactory>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<TicketFactory>();
        services.AddScoped<KitchenSync>();
        services.AddScoped<IKitchenService, KitchenService>();
        services.AddScoped<MenuChanges>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IStationService, StationService>();
        services.AddScoped<ITaxService, TaxService>();
        services.AddScoped<IModifierService, ModifierService>();
        services.AddScoped<IMenuItemService, MenuItemService>();
        services.AddScoped<IMenuQuery, MenuQuery>();
        services.AddScoped<SettingsService>();
        services.AddScoped<ISettingsService>(sp => sp.GetRequiredService<SettingsService>());
        services.AddScoped<ISystemInfoService>(sp => sp.GetRequiredService<SettingsService>());

        return services;
    }
}
