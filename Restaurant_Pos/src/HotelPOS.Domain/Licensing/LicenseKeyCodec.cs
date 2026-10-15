using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HotelPOS.Domain.Licensing;

// What the vendor signs. ExpiresOn null = perpetual. Dates are calendar dates: a licence is valid through its expiry day.
public sealed record LicensePayload(
    string LicenseId,
    string Customer,
    string MachineId,
    string Edition,
    int Terminals,
    DateOnly IssuedOn,
    DateOnly? ExpiresOn);

// HPOS1-<base64url payload JSON>.<base64url signature>. The signature is ECDSA P-256 over SHA-256 of the exact payload
// bytes (IEEE P1363 r||s). Keys travel as base64: the public key as SubjectPublicKeyInfo, the private as PKCS#8.
// The server only ever holds the public key; Sign exists for the vendor's generator and the tests.
public static class LicenseKeyCodec
{
    public const string Prefix = "HPOS1-";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static (string PrivateKey, string PublicKey) GenerateKeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey()), Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
    }

    public static string PublicKeyOf(string privateKey)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKey), out _);
        return Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
    }

    public static string Sign(LicensePayload payload, string privateKey)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKey), out _);
        var signature = ecdsa.SignData(bytes, HashAlgorithmName.SHA256);
        return Prefix + Base64Url(bytes) + "." + Base64Url(signature);
    }

    // Never throws: a pasted key may be anything. Whitespace and line breaks (keys get wrapped in e-mails) are ignored.
    public static bool TryVerify(string? key, string publicKey, out LicensePayload? payload, out string? error)
    {
        payload = null;
        error = null;
        var compact = new string((key ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray());
        var dot = compact.IndexOf('.');
        if (!compact.StartsWith(Prefix, StringComparison.Ordinal) || dot < 0)
        {
            error = "This is not a HotelPOS licence key.";
            return false;
        }

        try
        {
            var bytes = FromBase64Url(compact[Prefix.Length..dot]);
            var signature = FromBase64Url(compact[(dot + 1)..]);
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            if (!ecdsa.VerifyData(bytes, signature, HashAlgorithmName.SHA256))
            {
                error = "The licence key is not genuine (signature check failed).";
                return false;
            }

            payload = JsonSerializer.Deserialize<LicensePayload>(bytes, Json);
            if (payload is null || string.IsNullOrWhiteSpace(payload.MachineId) || payload.Terminals < 1)
            {
                payload = null;
                error = "The licence key is incomplete.";
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException or ArgumentException)
        {
            error = "The licence key is damaged. Copy it again exactly as received.";
            return false;
        }
    }

    public static string NormalizeMachineId(string? id) => (id ?? string.Empty).Trim().ToUpperInvariant();

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }
}

public static class MachineIds
{
    // "HP-" + 20 hex characters of SHA-256 over the raw hardware identity; stable across reboots and renames.
    public static string Compute(string rawHardwareIdentity)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("HotelPOS|" + rawHardwareIdentity));
        return "HP-" + Convert.ToHexString(hash, 0, 10);
    }
}
