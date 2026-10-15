using HotelPOS.Contracts.Cash;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Cash;

// The Day End record of one business day. While Closed, the day accepts no bill or payment changes; a reopened day can
// be closed again by a later Day End, which replaces the snapshot (the audit trail keeps every run).
public sealed class BusinessDayClose : IHasRowVersion
{
    private BusinessDayClose()
    {
    }

    public BusinessDayClose(DateOnly businessDay, int closedBy, DateTime nowUtc, string overridesJson, string totalsJson, string backupFile)
    {
        BusinessDay = businessDay;
        Apply(closedBy, nowUtc, overridesJson, totalsJson, backupFile);
    }

    public DateOnly BusinessDay { get; private set; }
    public BusinessDayStatus Status { get; private set; }
    public DateTime ClosedAt { get; private set; }
    public int ClosedBy { get; private set; }
    public string Overrides { get; private set; } = "[]";
    public string TotalsSnapshot { get; private set; } = "{}";
    public string? BackupFile { get; private set; }
    public string? PackFolder { get; private set; }
    public DateTime? ReopenedAt { get; private set; }
    public int? ReopenedBy { get; private set; }
    public string? ReopenReason { get; private set; }
    public uint RowVersion { get; set; }

    public bool IsClosed => Status == BusinessDayStatus.Closed;

    public void CloseAgain(int closedBy, DateTime nowUtc, string overridesJson, string totalsJson, string backupFile)
    {
        if (IsClosed)
        {
            throw new DomainException($"Business day {BusinessDay:yyyy-MM-dd} is already closed.", ErrorCodes.DayClosed);
        }

        Apply(closedBy, nowUtc, overridesJson, totalsJson, backupFile);
    }

    public void Reopen(int reopenedBy, string reason, DateTime nowUtc)
    {
        if (!IsClosed)
        {
            throw new DomainException($"Business day {BusinessDay:yyyy-MM-dd} is not closed.", ErrorCodes.InvalidStateTransition);
        }

        var cleanReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (cleanReason is null || cleanReason.Length > CashLimits.ReasonMaxLength)
        {
            throw new DomainException("Enter the reason for reopening the day.", ErrorCodes.ValidationError);
        }

        Status = BusinessDayStatus.Reopened;
        ReopenedAt = nowUtc;
        ReopenedBy = reopenedBy;
        ReopenReason = cleanReason;
    }

    public void RecordPack(string? folder) => PackFolder = folder;

    private void Apply(int closedBy, DateTime nowUtc, string overridesJson, string totalsJson, string backupFile)
    {
        Status = BusinessDayStatus.Closed;
        ClosedAt = nowUtc;
        ClosedBy = closedBy;
        Overrides = overridesJson;
        TotalsSnapshot = totalsJson;
        BackupFile = backupFile;
        PackFolder = null;
    }
}
