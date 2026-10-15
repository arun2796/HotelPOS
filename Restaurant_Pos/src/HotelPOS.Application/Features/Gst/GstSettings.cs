using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Contracts.Admin;
using HotelPOS.Domain.Billing;
using HotelPOS.Domain.Gst;

namespace HotelPOS.Application.Features.Gst;

// The restaurant's GST identity and the B2CL threshold, read from settings.
internal sealed record GstSettings(string Gstin, string? StateCode, string DefaultSac, decimal B2clThreshold)
{
    public bool GstinValid => Contracts.Gst.Gstin.IsValid(Gstin);

    public static async Task<GstSettings> ReadAsync(IAppDbContext db, CancellationToken cancellationToken)
    {
        var gstin = Contracts.Gst.Gstin.Normalize(await db.SettingAsync(SettingKeys.Gstin, cancellationToken));
        var sac = (await db.SettingAsync(SettingKeys.DefaultSacCode, cancellationToken))?.Trim();
        return new GstSettings(
            gstin,
            Contracts.Gst.Gstin.StateCode(gstin),
            string.IsNullOrEmpty(sac) ? "996331" : sac,
            await db.DecimalSettingAsync(SettingKeys.GstB2clThreshold, GstSupply.DefaultB2clThreshold, cancellationToken));
    }

    // Decides the bill's GSTR-1 table (see Bill.StampGst).
    public static async Task StampAsync(IAppDbContext db, Bill bill, CancellationToken cancellationToken)
    {
        var settings = await ReadAsync(db, cancellationToken);
        bill.StampGst(settings.StateCode, settings.B2clThreshold);
    }
}
