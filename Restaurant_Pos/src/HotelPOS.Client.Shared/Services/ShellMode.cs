using HotelPOS.Contracts.Security;

namespace HotelPOS.Client.Services;

/// <summary>Which set of screens (start page and bottom navigation) the signed-in user gets.</summary>
public enum ShellMode
{
    None,
    Waiter,
    Kitchen,
}

public static class ShellModes
{
    /// <summary>
    /// Decided from the server's roles and permissions, never the device type. The default Kitchen role also holds
    /// TablesView (to see where a ticket goes), so the Kitchen role wins over TablesView; a manager with both
    /// permissions but no Kitchen role works the floor.
    /// </summary>
    public static ShellMode For(WebAuthState auth)
    {
        if (auth.Has(Permissions.KitchenOperate) && (auth.IsInRole(Roles.Kitchen) || !auth.Has(Permissions.TablesView)))
        {
            return ShellMode.Kitchen;
        }

        return auth.Has(Permissions.TablesView) ? ShellMode.Waiter : ShellMode.None;
    }

    /// <summary>Avatar letters from the display name: "Asha Rao" is "AR"; a bracketed label is not a surname, so "Arun (Waiter)" is "A".</summary>
    public static string Initials(string? displayName)
    {
        var name = displayName ?? string.Empty;
        var bracket = name.IndexOf('(', StringComparison.Ordinal);
        if (bracket > 0)
        {
            name = name[..bracket];
        }

        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => char.IsLetterOrDigit(w[0])).ToList();
        return words.Count switch
        {
            0 => "?",
            1 => words[0][..1].ToUpperInvariant(),
            _ => $"{words[0][0]}{words[1][0]}".ToUpperInvariant(),
        };
    }
}
