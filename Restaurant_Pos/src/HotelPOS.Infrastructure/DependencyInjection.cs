using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Features.Reports;
using HotelPOS.Infrastructure.Documents;
using HotelPOS.Infrastructure.Identity;
using HotelPOS.Infrastructure.Licensing;
using HotelPOS.Infrastructure.Updates;
using HotelPOS.Infrastructure.Media;
using HotelPOS.Infrastructure.Persistence;
using HotelPOS.Infrastructure.Persistence.Interceptors;
using HotelPOS.Infrastructure.Persistence.Seed;
using HotelPOS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HotelPOS.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "HotelPOS";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));
        services.Configure<MediaOptions>(configuration.GetSection(MediaOptions.SectionName));
        services.Configure<BackupOptions>(configuration.GetSection(BackupOptions.SectionName));

        services.AddScoped<AuditableEntityInterceptor>();
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Connection string '{ConnectionStringName}' is not configured. Set ConnectionStrings:{ConnectionStringName} in appsettings.");
            }

            options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));
            options.AddInterceptors(sp.GetRequiredService<AuditableEntityInterceptor>());
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.TryAddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, PasswordHasherAdapter>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IMenuVersionStore, MenuVersionStore>();
        services.AddScoped<IInvoiceNumberService, InvoiceNumberService>();
        services.AddScoped<IReportQueries, ReportQueries>();
        services.AddHostedService<BillClaimCleanupService>();
        services.AddHostedService<AuditRetentionService>();
        services.AddScoped<IBackupService, BackupService>();
        services.AddScoped<IDayEndPackWriter, DayEndPackWriter>();
        services.AddScoped<IBusinessDayLock, BusinessDayLock>();
        services.AddScoped<IShiftLocks, ShiftLocks>();
        services.AddHostedService<BackupHostedService>();
        services.AddSingleton<IIdempotencyStore, IdempotencyStore>();
        services.AddHostedService<IdempotencyCleanupService>();
        services.AddSingleton<IMenuImageStore, MenuImageStore>();

        // Phase 16: licence machine id, import file reader, the client installer the server hosts under /updates/.
        services.Configure<UpdateOptions>(configuration.GetSection(UpdateOptions.SectionName));
        services.AddSingleton<IMachineIdProvider, HardwareMachineIdProvider>();
        services.AddSingleton<ITabularReader, TabularReader>();
        services.AddSingleton<IClientUpdateSource, ClientUpdateSource>();

        // Phase 14 documents: PDF (QuestPDF), Excel (ClosedXML), QR codes, logo, copy numbers.
        services.AddSingleton<IPdfRenderer, QuestPdfRenderer>();
        services.AddSingleton<IExcelExporter, ClosedXmlExporter>();
        services.AddSingleton<IQrCodeRenderer, QrCodeRenderer>();
        services.AddSingleton<ILogoStore, LogoStore>();
        services.AddScoped<IPrintCopyAllocator, PrintCopyAllocator>();
        services.AddScoped<DbSeeder>();

        return services;
    }
}
