using System.Globalization;
using System.Security.Cryptography;
using HotelPOS.Domain.Licensing;

namespace HotelPOS.LicenseGenerator;

public static class LicenseGeneratorCli
{
    private const string Usage = """
        HotelPOS licence generator (vendor tool; never ship it, never commit the private key)

          keygen  --out <private-key-file>
              Creates a new ECDSA P-256 key pair. The private key is written to the file (refused inside a git
              repository or over an existing file). Prints the PUBLIC key to paste into
              src/HotelPOS.Application/Features/Licensing/LicenseService.cs (EmbeddedLicenseKey.PublicKey).

          issue   --private-key <file> --machine <HP-id> --customer <name> [--edition Standard] [--terminals 5]
                  [--expires yyyy-MM-dd | perpetual] [--issued yyyy-MM-dd]
              Prints a licence key for the server whose Licence screen shows that machine id.

          inspect --public-key <base64 or file> <licence key>
              Verifies a key and prints what it says.

          sign-update --private-key <file> --manifest <latest.json>
              Signs the terminal update manifest in place (build/publish.ps1 runs this). The server advertises, and
              the terminals install, only a manifest that carries this signature.
        """;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            if (args.Length == 0)
            {
                output.WriteLine(Usage);
                return 1;
            }

            var options = Parse(args.Skip(1));
            return args[0].ToLowerInvariant() switch
            {
                "keygen" => KeyGen(options, output),
                "issue" => Issue(options, output),
                "inspect" => Inspect(options, output, error),
                "sign-update" => SignUpdate(options, output),
                _ => Fail(error, $"Unknown command '{args[0]}'.\n{Usage}"),
            };
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or FormatException or CryptographicException or UnauthorizedAccessException)
        {
            return Fail(error, ex.Message);
        }
    }

    private static int KeyGen(Dictionary<string, string> options, TextWriter output)
    {
        var path = Required(options, "out");
        if (File.Exists(path))
        {
            throw new ArgumentException($"{path} already exists; it is not overwritten.");
        }

        if (InsideGitRepository(path))
        {
            throw new ArgumentException("Refusing to write a private key inside a git repository. Choose a folder outside it, such as your user profile.");
        }

        var (privateKey, publicKey) = LicenseKeyCodec.GenerateKeyPair();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, ToPem(privateKey));
        output.WriteLine($"Private key written to {path}. Keep it secret and back it up; losing it means no more licences can be issued for builds with this public key.");
        output.WriteLine("Public key (embed in the server):");
        output.WriteLine(publicKey);
        return 0;
    }

    private static int Issue(Dictionary<string, string> options, TextWriter output)
    {
        var privateKey = FromPem(File.ReadAllText(Required(options, "private-key")));
        var machine = LicenseKeyCodec.NormalizeMachineId(Required(options, "machine"));
        if (!machine.StartsWith("HP-", StringComparison.Ordinal) || machine.Length != 23)
        {
            throw new ArgumentException("The machine id looks like HP- followed by 20 characters; copy it from the server's Licence screen.");
        }

        var terminals = int.Parse(options.GetValueOrDefault("terminals") ?? "5", CultureInfo.InvariantCulture);
        if (terminals is < 1 or > 500)
        {
            throw new ArgumentException("Terminals must be between 1 and 500.");
        }

        var issued = options.TryGetValue("issued", out var issuedText)
            ? DateOnly.ParseExact(issuedText, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : DateOnly.FromDateTime(DateTime.Today);
        DateOnly? expires = null;
        var expiresText = options.GetValueOrDefault("expires") ?? "perpetual";
        if (!expiresText.Equals("perpetual", StringComparison.OrdinalIgnoreCase))
        {
            expires = DateOnly.ParseExact(expiresText, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (expires < issued)
            {
                throw new ArgumentException("The expiry date is before the issue date.");
            }
        }

        var payload = new LicensePayload(
            Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            Required(options, "customer"),
            machine,
            options.GetValueOrDefault("edition") ?? "Standard",
            terminals,
            issued,
            expires);
        output.WriteLine(LicenseKeyCodec.Sign(payload, privateKey));
        return 0;
    }

    private static int Inspect(Dictionary<string, string> options, TextWriter output, TextWriter error)
    {
        var publicKey = Required(options, "public-key");
        if (File.Exists(publicKey))
        {
            publicKey = File.ReadAllText(publicKey).Trim();
        }

        if (!options.TryGetValue("_", out var key))
        {
            return Fail(error, "Give the licence key to inspect.");
        }

        if (!LicenseKeyCodec.TryVerify(key, publicKey, out var payload, out var problem))
        {
            return Fail(error, problem ?? "The key is not valid.");
        }

        output.WriteLine($"Valid. Customer {payload!.Customer}, machine {payload.MachineId}, edition {payload.Edition}, {payload.Terminals} terminal(s), "
            + $"issued {payload.IssuedOn:yyyy-MM-dd}, {(payload.ExpiresOn is { } e ? "expires " + e.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "perpetual")}, id {payload.LicenseId}.");
        return 0;
    }

    private static readonly System.Text.Json.JsonSerializerOptions ManifestJson = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private static int SignUpdate(Dictionary<string, string> options, TextWriter output)
    {
        var privateKey = FromPem(File.ReadAllText(Required(options, "private-key")));
        var path = Required(options, "manifest");
        var info = System.Text.Json.JsonSerializer.Deserialize<HotelPOS.Contracts.Admin.ClientUpdateInfo>(File.ReadAllText(path), ManifestJson)
            ?? throw new ArgumentException($"{path} is not an update manifest.");
        if (string.IsNullOrWhiteSpace(info.Version) || string.IsNullOrWhiteSpace(info.File) || string.IsNullOrWhiteSpace(info.Sha256) || info.Size <= 0)
        {
            throw new ArgumentException($"{path} must have version, file, sha256 and size before it can be signed.");
        }

        var signed = info with { Signature = HotelPOS.Contracts.Admin.UpdateManifestSignature.Sign(info, privateKey) };
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(signed, ManifestJson));
        output.WriteLine($"Signed {Path.GetFileName(path)} for HotelPOS {signed.Version} ({signed.File}).");
        return 0;
    }

    internal static string ToPem(string privateKeyBase64)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyBase64), out _);
        return ecdsa.ExportPkcs8PrivateKeyPem();
    }

    internal static string FromPem(string pem)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(pem);
        return Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey());
    }

    private static bool InsideGitRepository(string path)
    {
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return true;
            }
        }

        return false;
    }

    // --name value pairs; a lone argument is stored under "_" (the key for inspect).
    private static Dictionary<string, string> Parse(IEnumerable<string> args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var list = args.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].StartsWith("--", StringComparison.Ordinal))
            {
                map[list[i][2..]] = i + 1 < list.Count ? list[++i] : throw new ArgumentException($"{list[i]} needs a value.");
            }
            else
            {
                map["_"] = list[i];
            }
        }

        return map;
    }

    private static string Required(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException($"--{name} is required.");

    private static int Fail(TextWriter error, string message)
    {
        error.WriteLine(message);
        return 1;
    }
}
