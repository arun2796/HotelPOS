using System.IO;
using System.Text.RegularExpressions;

namespace HotelPOS.Desktop.Services.Configuration;

/// <summary>
/// File locations of this installation. A "profile" (command line: --profile NAME) lets several
/// terminals run side by side on one PC for testing; normal installations use the default profile.
/// </summary>
public sealed partial class AppPaths
{
    public const string DefaultProfile = "default";

    public AppPaths(string profile, string? machineRoot = null, string? userRoot = null)
    {
        Profile = profile;
        MachineRoot = machineRoot
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HotelPOS", "Desktop");
        UserRoot = userRoot
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HotelPOS");
    }

    public string Profile { get; }

    /// <summary>%ProgramData%\HotelPOS\Desktop — machine-wide configuration.</summary>
    public string MachineRoot { get; }

    /// <summary>%LocalAppData%\HotelPOS — per Windows user data.</summary>
    public string UserRoot { get; }

    private string Suffix => Profile == DefaultProfile ? string.Empty : "." + Profile;

    /// <summary>Preferred location of settings.json (shared by every Windows user of the PC).</summary>
    public string MachineSettingsFile => Path.Combine(MachineRoot, $"settings{Suffix}.json");

    /// <summary>Used when ProgramData is not writable (no installer ACL yet).</summary>
    public string UserSettingsFile => Path.Combine(UserRoot, "Desktop", $"settings{Suffix}.json");

    /// <summary>Session, preferences and (later) local drafts.</summary>
    public string UserDataDirectory => Path.Combine(UserRoot, Profile);

    public string LogDirectory => Path.Combine(UserRoot, "logs");

    public string LogFilePrefix => Profile == DefaultProfile ? "desktop" : $"desktop-{Profile}";

    public static AppPaths FromArgs(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], "--profile", StringComparison.OrdinalIgnoreCase) && SafeName().IsMatch(args[i + 1]))
            {
                return new AppPaths(args[i + 1].ToLowerInvariant());
            }
        }

        return new AppPaths(DefaultProfile);
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,40}$")]
    private static partial Regex SafeName();
}
