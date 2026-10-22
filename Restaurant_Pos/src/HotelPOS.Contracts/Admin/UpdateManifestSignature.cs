using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HotelPOS.Contracts.Admin;

/// <summary>
/// The vendor's PUBLIC key (ECDSA P-256, SubjectPublicKeyInfo, base64). Its private half signs licence keys and the
/// update manifest and lives only on the vendor's PC (tools/HotelPOS.LicenseGenerator keygen). Shared by the server
/// (licence check, manifest check) and the desktop terminal (manifest check before an installer runs).
/// </summary>
public static class VendorKeys
{
    public const string LicensePublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEgS5cEXcvD30rCmHFCQVqKZM2700s+cedERYP8J+6OgPyhbdPuCqwMg+W7hZ+ty3Bp/hUdjKdFEIV9jTU6h2uXA==";
}

/// <summary>
/// Signature over <c>/updates/latest.json</c>. The self-update downloads the installer from the server and runs it
/// elevated, so the manifest (and through its SHA-256 the installer) must come from the vendor, not merely from
/// whoever answers on port 5000: <c>publish.ps1</c> signs it with the vendor key, the server advertises only a
/// manifest that verifies, and the terminal verifies again before it starts the download.
/// Message: fixed prefix, version, file, upper-case SHA-256 and size, newline-separated; ECDSA P-256 with SHA-256.
/// </summary>
public static class UpdateManifestSignature
{
    private const string Prefix = "hotelpos-update-v1";

    public static byte[] Payload(ClientUpdateInfo info) =>
        Encoding.UTF8.GetBytes(string.Join('\n',
            Prefix,
            (info.Version ?? string.Empty).Trim(),
            (info.File ?? string.Empty).Trim(),
            (info.Sha256 ?? string.Empty).Trim().ToUpperInvariant(),
            info.Size.ToString(CultureInfo.InvariantCulture)));

    /// <summary>Signs with a PKCS#8 private key (base64); returns the signature as base64.</summary>
    public static string Sign(ClientUpdateInfo info, string privateKeyPkcs8Base64)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyPkcs8Base64), out _);
        return Convert.ToBase64String(ecdsa.SignData(Payload(info), HashAlgorithmName.SHA256));
    }

    /// <summary>True only for a manifest whose signature the public key (SubjectPublicKeyInfo, base64) verifies. Never throws.</summary>
    public static bool Verify(ClientUpdateInfo? info, string publicKeySpkiBase64)
    {
        if (info is null || string.IsNullOrWhiteSpace(info.Signature) || string.IsNullOrWhiteSpace(publicKeySpkiBase64))
        {
            return false;
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeySpkiBase64), out _);
            return ecdsa.VerifyData(Payload(info), Convert.FromBase64String(info.Signature.Trim()), HashAlgorithmName.SHA256);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            return false;
        }
    }
}
