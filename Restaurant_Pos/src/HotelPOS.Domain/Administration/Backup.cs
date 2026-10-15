using HotelPOS.Contracts.Enums;
using HotelPOS.Domain.Common;

namespace HotelPOS.Domain.Administration;

public sealed class Backup
{
    private Backup()
    {
    }

    public Backup(string fileName, BackupTrigger trigger, string? triggeredBy, DateTime startedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new DomainException("A backup needs a file name.");
        }

        FileName = fileName;
        Trigger = trigger;
        TriggeredBy = triggeredBy;
        StartedAt = startedAtUtc;
        Status = BackupStatus.Running;
    }

    public int Id { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string? MediaFileName { get; private set; }
    public long SizeBytes { get; private set; }
    public BackupStatus Status { get; private set; }
    public BackupTrigger Trigger { get; private set; }
    public string? TriggeredBy { get; private set; }
    public DateTime StartedAt { get; private set; }
    public DateTime? FinishedAt { get; private set; }
    public string? Error { get; private set; }

    public void Succeed(long sizeBytes, string? mediaFileName, DateTime finishedAtUtc)
    {
        EnsureRunning();
        Status = BackupStatus.Succeeded;
        SizeBytes = sizeBytes;
        MediaFileName = mediaFileName;
        FinishedAt = finishedAtUtc;
    }

    public void Fail(string error, DateTime finishedAtUtc)
    {
        EnsureRunning();
        Status = BackupStatus.Failed;
        Error = error.Length <= 1000 ? error : error[..1000];
        FinishedAt = finishedAtUtc;
    }

    private void EnsureRunning()
    {
        if (Status != BackupStatus.Running)
        {
            throw new DomainException($"Backup {FileName} has already finished ({Status}).");
        }
    }
}
