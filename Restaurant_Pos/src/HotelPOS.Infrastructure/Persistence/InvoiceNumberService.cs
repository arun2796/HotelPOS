using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Admin;
using HotelPOS.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Infrastructure.Persistence;

public sealed class InvoiceNumberService : IInvoiceNumberService
{
    private readonly AppDbContext _db;

    public InvoiceNumberService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<string> NextAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        if (_db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Invoice numbers are gap-free only inside the finalisation transaction.");
        }

        var settings = await _db.Settings.AsNoTracking()
            .Where(s => s.Key == SettingKeys.InvoicePrefix || s.Key == SettingKeys.InvoiceResetPolicy)
            .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
        var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, TimeZoneInfo.Local);
        var period = InvoiceNumberFormatter.PeriodKey(settings.GetValueOrDefault(SettingKeys.InvoiceResetPolicy), local);

        // The upsert locks the period's row until commit, so concurrent finalisations take numbers one after another
        // and a rollback hands the number back.
        var numbers = await _db.Database.SqlQuery<long>($"""
            INSERT INTO "InvoiceCounters" ("Period", "LastNumber") VALUES ({period}, 1)
            ON CONFLICT ("Period") DO UPDATE SET "LastNumber" = "InvoiceCounters"."LastNumber" + 1
            RETURNING "LastNumber" AS "Value"
            """).ToListAsync(cancellationToken);

        return InvoiceNumberFormatter.Format(settings.GetValueOrDefault(SettingKeys.InvoicePrefix), local, numbers.Single());
    }
}
