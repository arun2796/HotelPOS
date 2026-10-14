using System.Reflection;
using FluentValidation;
using HotelPOS.Application.Common.Behaviors;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Administration;
using HotelPOS.Application.Auth;
using HotelPOS.Application.Features.Billing;
using HotelPOS.Application.Devices;
using HotelPOS.Application.Features.Floor;
using HotelPOS.Application.Features.Kitchen;
using HotelPOS.Application.Features.Menu;
using HotelPOS.Application.Features.Orders;
using HotelPOS.Application.Features.Print;
using HotelPOS.Application.Features.Reports;
using HotelPOS.Application.Security;
using HotelPOS.Application.Settings;
using HotelPOS.Application.Users;
using Microsoft.Extensions.DependencyInjection;

namespace HotelPOS.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        services.AddInputValidators(assembly);
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);

            // Registration order is execution order, outermost first.
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(AuthorizationBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
            cfg.AddOpenBehavior(typeof(AuditBehavior<,>));
        });
        services.AddScoped<AuditTrail>();
        services.AddScoped<IAuditTrail>(sp => sp.GetRequiredService<AuditTrail>());
        services.AddScoped<AfterCommitQueue>();
        services.AddScoped<IAfterCommit>(sp => sp.GetRequiredService<AfterCommitQueue>());

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IDeviceService, DeviceService>();
        services.AddScoped<FloorSections>();
        services.AddScoped<TableEvents>();
        services.AddScoped<FloorTables>();
        services.AddScoped<OrderItemFactory>();
        services.AddScoped<OrderSteps>();
        services.AddScoped<OrderEvents>();
        services.AddScoped<TicketFactory>();
        services.AddScoped<KitchenSync>();
        services.AddScoped<KitchenTickets>();
        services.AddScoped<ManagerApprovals>();
        services.AddSingleton<IDeviceAccessCache, DeviceAccessCache>();
        services.AddSingleton<IUserAccessCache, UserAccessCache>();
        services.AddScoped<BillSteps>();
        services.AddScoped<BillEvents>();
        services.AddScoped<BillWorkflow>();
        services.AddScoped<PrintSources>();
        services.AddScoped<IBusinessDays, BusinessDays>();
        services.AddScoped<ReportRanges>();
        services.AddScoped<IServerNoticeService, ServerNoticeService>();
        services.AddScoped<MenuChanges>();
        services.AddScoped<MenuCategories>();
        services.AddScoped<MenuItems>();
        services.AddScoped<MenuModifiers>();
        services.AddScoped<MenuStations>();
        services.AddScoped<MenuTaxes>();
        services.AddScoped<SettingsService>();
        services.AddScoped<ISettingsService>(sp => sp.GetRequiredService<SettingsService>());
        services.AddScoped<ISystemInfoService>(sp => sp.GetRequiredService<SettingsService>());
        services.AddScoped<HotelPOS.Application.Security.IPermissionService, HotelPOS.Application.Security.PermissionService>();
        services.AddScoped<HotelPOS.Application.Security.IApprovalTokenService, HotelPOS.Application.Security.ApprovalTokenService>();
        services.AddScoped<HotelPOS.Application.Security.ISessionService, HotelPOS.Application.Security.SessionService>();
        services.AddScoped<IDeviceAdminService, DeviceAdminService>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();

        return services;
    }

    // Each IHasInput<TInput> request is validated by the validators of its TInput (see InputValidator).
    internal static IServiceCollection AddInputValidators(this IServiceCollection services, Assembly assembly)
    {
        var requests = assembly.GetTypes().Where(t => t is { IsAbstract: false, IsGenericTypeDefinition: false });
        foreach (var request in requests)
        {
            var inputs = request.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IHasInput<>));
            foreach (var input in inputs)
            {
                services.AddTransient(
                    typeof(IValidator<>).MakeGenericType(request),
                    typeof(InputValidator<,>).MakeGenericType(request, input.GetGenericArguments()[0]));
            }
        }

        return services;
    }
}
