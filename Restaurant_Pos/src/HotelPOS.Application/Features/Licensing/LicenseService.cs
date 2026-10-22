using HotelPOS.Application.Common;
using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Common;
using HotelPOS.Domain.Identity;
using HotelPOS.Domain.Licensing;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Licensing;

public static class EmbeddedLicenseKey
{
    // The vendor's PUBLIC key. The private half lives only on the vendor's PC (tools/HotelPOS.LicenseGenerator keygen).
    // One constant for the server and the terminals (update manifest check): Contracts.Admin.VendorKeys.
    public const string PublicKey = Contracts.Admin.VendorKeys.LicensePublicKey;
}

internal sealed class EmbeddedLicenseTrust : ILicenseTrust
{
    public string PublicKey => EmbeddedLicenseKey.PublicKey;
}

public interface ILicenseService
{
    Task<LicenseEvaluation> EvaluateAsync(CancellationToken cancellationToken);

    Task<LicenseStatusDto> GetStatusAsync(CancellationToken cancellationToken);

    // Null when the terminal may sign in. A new name over the limit is refused before it is registered.
    Task<AppError?> CheckTerminalAsync(string deviceName, CancellationToken cancellationToken);

    Task<Result<LicenseStatusDto>> ApplyAsync(string key, int? userId, CancellationToken cancellationToken);
}

internal sealed class LicenseService : ILicenseService
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly IMachineIdProvider _machine;
    private readonly ILicenseTrust _trust;

    public LicenseService(IAppDbContext db, IClock clock, IMachineIdProvider machine, ILicenseTrust trust)
    {
        _db = db;
        _clock = clock;
        _machine = machine;
        _trust = trust;
    }

    public async Task<LicenseEvaluation> EvaluateAsync(CancellationToken cancellationToken)
    {
        var row = await _db.ServerLicenses.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var now = _clock.UtcNow;
        LicensePayload? licence = null;
        string? problem = null;
        if (row?.Key is { } key)
        {
            licence = Verify(key, out problem);
        }

        return LicenseEvaluator.Evaluate(now, Today(now), row?.TrialStartedAt ?? now, licence, problem);
    }

    public async Task<LicenseStatusDto> GetStatusAsync(CancellationToken cancellationToken)
    {
        var evaluation = await EvaluateAsync(cancellationToken);
        var inUse = await _db.Devices.AsNoTracking().CountAsync(d => d.IsActive, cancellationToken);
        return ToDto(evaluation, inUse);
    }

    public async Task<AppError?> CheckTerminalAsync(string deviceName, CancellationToken cancellationToken)
    {
        var evaluation = await EvaluateAsync(cancellationToken);
        var active = await _db.Devices.AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.RegisteredAt).ThenBy(d => d.Id)
            .Select(d => d.Name)
            .ToListAsync(cancellationToken);

        var position = active.IndexOf(Device.Normalize(deviceName));
        var allowed = position >= 0 ? position < evaluation.Terminals : active.Count < evaluation.Terminals;
        return allowed
            ? null
            : new AppError(ErrorCodes.LicenseTerminalLimit,
                $"This licence covers {evaluation.Terminals} terminal(s) and {active.Count} are registered. "
                + "Ask an administrator to deactivate a terminal that is not used, or to extend the licence.");
    }

    public async Task<Result<LicenseStatusDto>> ApplyAsync(string key, int? userId, CancellationToken cancellationToken)
    {
        var payload = Verify(key, out var problem);
        if (payload is null)
        {
            return new AppError(ErrorCodes.LicenseInvalid, problem ?? "The licence key was not accepted.");
        }

        var now = _clock.UtcNow;
        if (payload.ExpiresOn is { } expiry && expiry < Today(now))
        {
            return new AppError(ErrorCodes.LicenseInvalid, $"This licence key expired on {expiry:dd-MM-yyyy}.");
        }

        var row = await _db.ServerLicenses.FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            row = new ServerLicense(now);
            _db.ServerLicenses.Add(row);
        }

        row.Apply(Compact(key), now, userId);
        await _db.SaveChangesAsync(cancellationToken);
        return await GetStatusAsync(cancellationToken);
    }

    public static string Compact(string key) => new(key.Where(c => !char.IsWhiteSpace(c)).ToArray());

    private LicensePayload? Verify(string key, out string? problem)
    {
        if (!LicenseKeyCodec.TryVerify(key, _trust.PublicKey, out var payload, out problem))
        {
            return null;
        }

        if (LicenseKeyCodec.NormalizeMachineId(payload!.MachineId) != LicenseKeyCodec.NormalizeMachineId(_machine.GetMachineId()))
        {
            problem = $"This licence was issued for another PC ({payload.MachineId}). This server is {_machine.GetMachineId()}.";
            return null;
        }

        return payload;
    }

    private LicenseStatusDto ToDto(LicenseEvaluation evaluation, int terminalsInUse) => new()
    {
        State = evaluation.State switch
        {
            LicenseState.Licensed => LicenseStateNames.Licensed,
            LicenseState.Expired => LicenseStateNames.Expired,
            _ => LicenseStateNames.Trial,
        },
        Edition = evaluation.Edition,
        Customer = evaluation.Customer,
        MaxTerminals = evaluation.Terminals,
        TerminalsInUse = terminalsInUse,
        ExpiresOn = evaluation.ExpiresOn,
        DaysRemaining = evaluation.DaysRemaining,
        IsExpired = evaluation.IsExpired,
        ShowWarning = evaluation.State == LicenseState.Trial
            || evaluation.IsExpired
            || (evaluation.DaysRemaining is { } days && days <= LicenseLimits.WarnDays),
        MachineId = _machine.GetMachineId(),
        Message = evaluation.Problem,
    };

    private static DateOnly Today(DateTime nowUtc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, TimeZoneInfo.Local));
}
