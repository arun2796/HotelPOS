using System.Globalization;
using System.Text;
using System.Text.Json;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Presentation;

namespace HotelPOS.Desktop.Modules.Dashboard;

/// <summary>
/// Turns an audit-log row ("Payment.Received" on Bill 41 by cashier1) into the sentence the dashboard's Recent activity
/// shows ("cashier1 received a payment on bill 41 · BILLING-01"). Presentation only: nothing is inferred that the row
/// does not carry; an action without a known verb is spelled out from its name.
/// </summary>
public static class AuditSentences
{
    // Verbs for the actions in Application/Common/AuditActions.cs. The entity is appended by the caller.
    private static readonly IReadOnlyDictionary<string, string> Verbs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Created"] = "created", ["Updated"] = "updated", ["Activated"] = "activated", ["Deactivated"] = "deactivated",
        ["Registered"] = "registered", ["Submitted"] = "submitted", ["Cancelled"] = "cancelled", ["Served"] = "served",
        ["Accepted"] = "accepted", ["Ready"] = "marked ready", ["Completed"] = "completed", ["Received"] = "received a payment on",
        ["Refunded"] = "refunded", ["Voided"] = "voided", ["Reprinted"] = "reprinted", ["DiscountApplied"] = "applied a discount to",
        ["DiscountRemoved"] = "removed the discount from", ["PriceChanged"] = "changed the price of", ["ImageChanged"] = "changed the picture of",
        ["RoleChanged"] = "changed the role of", ["PasswordReset"] = "reset the password of", ["PasswordChanged"] = "changed the password of",
        ["LockedOut"] = "locked out", ["Unlocked"] = "unlocked", ["LoggedOutAll"] = "signed out every session of",
        ["PermissionsUpdated"] = "changed the permissions of", ["LoginSucceeded"] = "signed in", ["LoginFailed"] = "failed to sign in",
        ["RefreshTokenReuse"] = "reused a refresh token", ["TokenIssued"] = "approved", ["Rejected"] = "rejected",
        ["OutOfService"] = "took out of service", ["InService"] = "put back in service", ["ReleasedByManager"] = "released",
        ["Opened"] = "opened", ["Closed"] = "closed", ["Reopened"] = "reopened", ["ForceClosed"] = "force-closed",
        ["HandedOver"] = "handed over", ["MovementAdded"] = "recorded a cash movement on", ["NoticeSent"] = "sent a notice from",
        ["Failed"] = "failed", ["Purged"] = "purged", ["VersionSaved"] = "saved a version of", ["LogoChanged"] = "changed the logo of",
        ["Imported"] = "imported", ["Undone"] = "undid the import of", ["Applied"] = "applied",
    };

    /// <summary>The sentence for a row: who, what, which entity (and the device, when known).</summary>
    public static string Describe(AuditLogDto row)
    {
        var who = string.IsNullOrWhiteSpace(row.UserName) ? "System" : row.UserName;
        var (prefix, verbWord) = Split(row.Action, row.EntityType);
        // The row's entity type is what the action was done to (Payment.Received is logged on the Bill); the action's
        // prefix is the fallback for rows without one.
        var entityWord = string.IsNullOrWhiteSpace(row.EntityType) ? prefix : row.EntityType;
        var verb = Verbs.TryGetValue(verbWord, out var known) ? known : Humanize(verbWord).ToLowerInvariant();
        var builder = new StringBuilder(who).Append(' ').Append(verb);

        // "signed in" / "failed to sign in" stand alone; everything else names the entity.
        if (verbWord is not ("LoginSucceeded" or "LoginFailed" or "RefreshTokenReuse"))
        {
            builder.Append(' ').Append(Humanize(entityWord).ToLowerInvariant());
            var reference = Reference(row);
            if (reference is not null)
            {
                builder.Append(' ').Append(reference);
            }
        }

        if (!string.IsNullOrWhiteSpace(row.DeviceName))
        {
            builder.Append(" · ").Append(row.DeviceName);
        }

        return builder.ToString();
    }

    /// <summary>The tone a row is shown in: failures and lock-outs red, deactivations and cancellations amber, the rest neutral.</summary>
    public static StatusTone ToneFor(string action)
    {
        var verb = Split(action, string.Empty).Verb;
        return verb switch
        {
            "LoginFailed" or "Failed" or "RefreshTokenReuse" or "LockedOut" or "Voided" or "Purged" => StatusTone.Danger,
            "Cancelled" or "Deactivated" or "OutOfService" or "ForceClosed" or "Rejected" or "Refunded" => StatusTone.Warning,
            "Received" or "Completed" or "Ready" or "Activated" or "Created" or "LoginSucceeded" => StatusTone.Success,
            _ => StatusTone.Neutral,
        };
    }

    public static string TimeText(DateTime timestampUtc) => timestampUtc.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);

    // "Payment.Received" → ("Payment", "Received"); a one-word action uses the row's entity type.
    private static (string Entity, string Verb) Split(string action, string entityType)
    {
        var dot = action.LastIndexOf('.');
        return dot > 0 && dot < action.Length - 1
            ? (action[..dot], action[(dot + 1)..])
            : (string.IsNullOrWhiteSpace(entityType) ? "record" : entityType, action);
    }

    // The most readable handle the row carries: an invoice/order/ticket number from NewValues, else the entity id.
    private static string? Reference(AuditLogDto row)
    {
        foreach (var key in new[] { "invoiceNumber", "orderNumber", "ticketNumber", "name", "username", "code" })
        {
            if (TryRead(row.NewValues, key, out var value) || TryRead(row.OldValues, key, out value))
            {
                return value;
            }
        }

        return string.IsNullOrWhiteSpace(row.EntityId) ? null : row.EntityId;
    }

    private static bool TryRead(string? json, string key, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty(key, out var element)
                && element.ValueKind is JsonValueKind.String or JsonValueKind.Number)
            {
                value = element.ToString();
                return value.Length > 0;
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }

    // "MenuItem" → "Menu item", "PriceChanged" → "price changed".
    private static string Humanize(string pascal)
    {
        if (string.IsNullOrEmpty(pascal))
        {
            return pascal;
        }

        var builder = new StringBuilder(pascal.Length + 4);
        for (var i = 0; i < pascal.Length; i++)
        {
            var c = pascal[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(pascal[i - 1]))
            {
                builder.Append(' ').Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
