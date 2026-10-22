namespace HotelPOS.Contracts.Admin;

public static class LicenseStateNames
{
    public const string Trial = "Trial";
    public const string Licensed = "Licensed";
    public const string Expired = "Expired";
}

public sealed record LicenseStatusDto
{
    public string State { get; init; } = LicenseStateNames.Trial;
    public string Edition { get; init; } = string.Empty;
    public string? Customer { get; init; }
    public int MaxTerminals { get; init; }
    public int TerminalsInUse { get; init; }
    public DateOnly? ExpiresOn { get; init; }
    public int? DaysRemaining { get; init; }
    public bool IsExpired { get; init; }

    // True during the trial and in the last days before a licence expires: the desktop shows its banner.
    public bool ShowWarning { get; init; }
    public string MachineId { get; init; } = string.Empty;
    public string? Message { get; init; }
}

public sealed record ApplyLicenseRequest
{
    public string Key { get; init; } = string.Empty;
}

public static class LicenseLimitsDto
{
    public const int MaxKeyLength = 4000;
}

// What a terminal needs to update itself (also read from /updates/latest.json on the server).
public sealed record ClientUpdateInfo
{
    public string Version { get; init; } = string.Empty;
    public string File { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long Size { get; init; }

    // ECDSA signature (base64) by the vendor over version, file, sha256 and size (UpdateManifestSignature); terminals
    // install nothing without it, so a spoofed server on the staff Wi-Fi cannot push an installer.
    public string? Signature { get; init; }
}
