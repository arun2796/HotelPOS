using System.IO;
using System.Text.RegularExpressions;

namespace HotelPOS.Desktop.Services.Configuration;

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

    public string MachineRoot { get; }

    public string UserRoot { get; }

    private string Suffix => Profile == DefaultProfile ? string.Empty : "." + Profile;

    public string MachineSettingsFile => Path.Combine(MachineRoot, $"settings{Suffix}.json");

    public string UserSettingsFile => Path.Combine(UserRoot, "Desktop", $"settings{Suffix}.json");

    public string UserDataDirectory => Path.Combine(UserRoot, Profile);

    public string LogDirectory => Path.Combine(UserRoot, "logs");

    public string ImageCacheDirectory => Path.Combine(UserRoot, "cache", "images");

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
