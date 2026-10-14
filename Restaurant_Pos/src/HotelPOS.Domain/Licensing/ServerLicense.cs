namespace HotelPOS.Domain.Licensing;

// The one row (Id 1) that holds the trial start and the applied licence key. The migration creates it.
public sealed class ServerLicense
{
    public const int SingletonId = 1;

    private ServerLicense()
    {
    }

    public ServerLicense(DateTime trialStartedAt)
    {
        Id = SingletonId;
        TrialStartedAt = trialStartedAt;
    }

    public int Id { get; private set; }
    public DateTime TrialStartedAt { get; private set; }
    public string? Key { get; private set; }
    public DateTime? AppliedAt { get; private set; }
    public int? AppliedBy { get; private set; }

    public void Apply(string key, DateTime nowUtc, int? userId)
    {
        Key = key;
        AppliedAt = nowUtc;
        AppliedBy = userId;
    }
}
