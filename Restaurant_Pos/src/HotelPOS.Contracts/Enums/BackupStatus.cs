namespace HotelPOS.Contracts.Enums;

public enum BackupStatus
{
    Running = 1,
    Succeeded = 2,
    Failed = 3,
}

public enum BackupTrigger
{
    Schedule = 1,
    Manual = 2,
}
