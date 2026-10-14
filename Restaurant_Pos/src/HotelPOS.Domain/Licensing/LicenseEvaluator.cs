namespace HotelPOS.Domain.Licensing;

public enum LicenseState
{
    Trial,
    Licensed,
    Expired,
}

public sealed record LicenseEvaluation(
    LicenseState State,
    string Edition,
    string? Customer,
    int Terminals,
    DateOnly? ExpiresOn,
    int? DaysRemaining,
    string? Problem)
{
    // Expired means read-only for everyone and sign-in for administrators only.
    public bool IsExpired => State == LicenseState.Expired;
}

public static class LicenseLimits
{
    public const int TrialDays = 7;
    public const int TrialTerminals = 10;
    public const int WarnDays = 14;
    public const string TrialEdition = "Trial";
}

public static class LicenseEvaluator
{
    // storedKeyProblem: why a stored key was not accepted (bad signature, other machine), or null when there is none.
    public static LicenseEvaluation Evaluate(DateTime nowUtc, DateOnly today, DateTime trialStartedAtUtc, LicensePayload? licence, string? storedKeyProblem)
    {
        if (licence is not null)
        {
            if (licence.ExpiresOn is not { } expiry)
            {
                return new LicenseEvaluation(LicenseState.Licensed, licence.Edition, licence.Customer, licence.Terminals, null, null, null);
            }

            var days = expiry.DayNumber - today.DayNumber;
            return days < 0
                ? new LicenseEvaluation(LicenseState.Expired, licence.Edition, licence.Customer, licence.Terminals, expiry, 0,
                    $"The licence expired on {expiry:dd-MM-yyyy}.")
                : new LicenseEvaluation(LicenseState.Licensed, licence.Edition, licence.Customer, licence.Terminals, expiry, days, null);
        }

        var trialEnd = trialStartedAtUtc.AddDays(LicenseLimits.TrialDays);
        var remaining = trialEnd - nowUtc;
        var trialDays = Math.Max(0, (int)Math.Ceiling(remaining.TotalDays));
        var trialExpiry = DateOnly.FromDateTime(trialEnd.ToLocalTime());
        return remaining <= TimeSpan.Zero
            ? new LicenseEvaluation(LicenseState.Expired, LicenseLimits.TrialEdition, null, LicenseLimits.TrialTerminals, trialExpiry, 0,
                storedKeyProblem ?? "The 7-day trial has ended. Apply a licence key to continue.")
            : new LicenseEvaluation(LicenseState.Trial, LicenseLimits.TrialEdition, null, LicenseLimits.TrialTerminals, trialExpiry, trialDays, storedKeyProblem);
    }
}
