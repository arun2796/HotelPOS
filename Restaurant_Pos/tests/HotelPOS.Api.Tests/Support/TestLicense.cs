using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Domain.Licensing;

namespace HotelPOS.Api.Tests.Support;

// The API tests run against their own licence key pair and a fixed machine id, so they can issue keys without the
// vendor's private key (which is never in the repository).
public static class TestLicense
{
    public const string MachineId = "HP-0123456789ABCDEF0123";

    private static readonly (string PrivateKey, string PublicKey) Pair = LicenseKeyCodec.GenerateKeyPair();

    public static string PublicKey => Pair.PublicKey;

    public static string Issue(int terminals, DateOnly? expiresOn, string machineId = MachineId, string edition = "Standard") =>
        LicenseKeyCodec.Sign(
            new LicensePayload(Guid.NewGuid().ToString("N")[..12], "Test Restaurant", machineId, edition, terminals, DateOnly.FromDateTime(DateTime.Today), expiresOn),
            Pair.PrivateKey);

    // updates/latest.json as publish.ps1 writes it: signed with the vendor key (here the tests' pair).
    public static string SignedManifest(HotelPOS.Contracts.Admin.ClientUpdateInfo info) =>
        System.Text.Json.JsonSerializer.Serialize(
            info with { Signature = HotelPOS.Contracts.Admin.UpdateManifestSignature.Sign(info, Pair.PrivateKey) },
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

    public sealed class FixedMachine : IMachineIdProvider
    {
        public string GetMachineId() => MachineId;
    }

    public sealed class FixedTrust : ILicenseTrust
    {
        public string PublicKey => Pair.PublicKey;
    }
}
