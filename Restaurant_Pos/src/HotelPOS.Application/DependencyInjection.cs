using System.Reflection;
using FluentValidation;
using HotelPOS.Application.Common.Behaviors;
using HotelPOS.Application.Common.Cqrs;
using HotelPOS.Application.Features.Auth;
using HotelPOS.Application.Features.Billing;
using HotelPOS.Application.Features.DayEnd;
using HotelPOS.Application.Features.Devices;
using HotelPOS.Application.Features.Floor;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Features.Gst;
using HotelPOS.Application.Features.Licensing;
using HotelPOS.Application.Features.Import;
using HotelPOS.Application.Features.Gst.Queries;
using HotelPOS.Application.Features.PeriodClosing;
using HotelPOS.Application.Features.Kitchen;
using HotelPOS.Application.Features.Menu;
using HotelPOS.Application.Features.Orders;
using HotelPOS.Application.Features.Print;
using HotelPOS.Application.Features.Reports;
using HotelPOS.Application.Features.Security;
using HotelPOS.Application.Features.Templates;
using HotelPOS.Application.Features.Shifts;
using HotelPOS.Application.Features.Users;
using HotelPOS.Application.Security;
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
            cfg.AddOpenBehavior(typeof(BusinessDayLockBehavior<,>));
            cfg.AddOpenBehavior(typeof(AuditBehavior<,>));
        });
        services.AddScoped<AuditTrail>();
        services.AddScoped<IAuditTrail>(sp => sp.GetRequiredService<AuditTrail>());
        services.AddScoped<AfterCommitQueue>();
        services.AddScoped<IAfterCommit>(sp => sp.GetRequiredService<AfterCommitQueue>());

        // Steps shared by the handlers of a feature.
        services.AddScoped<AuthTokens>();
        services.AddScoped<UserAdministration>();
        services.AddScoped<RolePermissions>();
        services.AddScoped<IPasswordPolicySource, PasswordPolicySource>();
        services.AddScoped<ApprovalTokens>();
        services.AddScoped<DeviceRegistry>();
        services.AddScoped<FloorSections>();
        services.AddScoped<FloorTables>();
        services.AddScoped<TableEvents>();
        services.AddScoped<MenuChanges>();
        services.AddScoped<MenuCategories>();
        services.AddScoped<MenuItems>();
        services.AddScoped<MenuModifiers>();
        services.AddScoped<MenuStations>();
        services.AddScoped<MenuTaxes>();
        services.AddScoped<OrderItemFactory>();
        services.AddScoped<OrderSteps>();
        services.AddScoped<OrderEvents>();
        services.AddScoped<TicketFactory>();
        services.AddScoped<KitchenSync>();
        services.AddScoped<KitchenTickets>();
        services.AddScoped<ManagerApprovals>();
        services.AddScoped<BillSteps>();
        services.AddScoped<BillEvents>();
        services.AddScoped<BillWorkflow>();
        services.AddScoped<PrintSources>();
        services.AddScoped<PrintDocuments>();
        services.AddScoped<PrintLog>();
        services.AddScoped<DocumentTemplates>();
        services.AddScoped<TemplateActivation>();
        services.AddScoped<IBusinessDays, BusinessDays>();
        services.AddScoped<ReportRanges>();
        services.AddScoped<ShiftSteps>();
        services.AddScoped<ShiftClosing>();
        services.AddScoped<ShiftEvents>();
        services.AddScoped<DayEndChecks>();
        services.AddScoped<DayEndPack>();
        services.AddScoped<GstDataLoader>();
        services.AddScoped<GstPeriodReader>();
        services.AddScoped<GstExports>();
        services.AddScoped<PeriodChecks>();
        services.AddScoped<PeriodEvents>();
        services.AddScoped<DayEndRecords>();
        services.AddScoped<ILicenseService, LicenseService>();
        services.AddSingleton<ILicenseTrust, EmbeddedLicenseTrust>();
        services.AddScoped<ImportSession>();
        services.AddScoped<ImportExecutor>();

        // Read by the access middleware on every request.
        services.AddSingleton<IDeviceAccessCache, DeviceAccessCache>();
        services.AddSingleton<IUserAccessCache, UserAccessCache>();

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
