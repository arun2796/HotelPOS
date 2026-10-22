using HotelPOS.Domain.Licensing;
using HotelPOS.LicenseGenerator;

namespace HotelPOS.Domain.Tests.Licensing;

public class LicenseTests
{
    private const string Machine = "HP-0123456789ABCDEF0123";

    private static readonly (string PrivateKey, string PublicKey) Pair = LicenseKeyCodec.GenerateKeyPair();

    private static LicensePayload Payload(DateOnly? expires = null, string machine = Machine, int terminals = 5) =>
        new("LIC000000001", "Hotel Saravana", machine, "Standard", terminals, new DateOnly(2026, 10, 1), expires);

    [Fact]
    public void SignedKey_Verifies_AndReturnsThePayload()
    {
        var key = LicenseKeyCodec.Sign(Payload(new DateOnly(2027, 10, 1)), Pair.PrivateKey);

        LicenseKeyCodec.TryVerify(key, Pair.PublicKey, out var payload, out var error).Should().BeTrue(error);

        payload.Should().Be(Payload(new DateOnly(2027, 10, 1)));
        key.Should().StartWith("HPOS1-");
    }

    [Fact]
    public void Key_WithWrappedLines_StillVerifies()
    {
        var key = LicenseKeyCodec.Sign(Payload(), Pair.PrivateKey);
        var wrapped = string.Join("\r\n  ", Enumerable.Range(0, (key.Length + 39) / 40).Select(i => key.Substring(i * 40, Math.Min(40, key.Length - i * 40))));

        LicenseKeyCodec.TryVerify(wrapped, Pair.PublicKey, out _, out _).Should().BeTrue();
    }

    [Fact]
    public void TamperedPayload_IsRejected()
    {
        var key = LicenseKeyCodec.Sign(Payload(new DateOnly(2026, 11, 1), terminals: 2), Pair.PrivateKey);
        var other = LicenseKeyCodec.Sign(Payload(null, terminals: 500), Pair.PrivateKey);

        // The payload of one valid key with the signature of another.
        var forged = other[..other.IndexOf('.')] + key[key.IndexOf('.')..];

        LicenseKeyCodec.TryVerify(forged, Pair.PublicKey, out var payload, out var error).Should().BeFalse();
        payload.Should().BeNull();
        error.Should().Contain("not genuine");
    }

    [Fact]
    public void KeySignedByAnotherVendorKey_IsRejected()
    {
        var other = LicenseKeyCodec.GenerateKeyPair();
        var key = LicenseKeyCodec.Sign(Payload(), other.PrivateKey);

        LicenseKeyCodec.TryVerify(key, Pair.PublicKey, out _, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("HPOS1-")]
    [InlineData("HPOS1-abc.def")]
    [InlineData("HPOS1-!!!.???")]
    public void Garbage_NeverThrows_AndIsRejected(string text)
    {
        LicenseKeyCodec.TryVerify(text, Pair.PublicKey, out _, out var error).Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void MachineId_IsStableAndShaped()
    {
        var first = MachineIds.Compute("guid-1|cpu");

        first.Should().Be(MachineIds.Compute("guid-1|cpu"));
        first.Should().NotBe(MachineIds.Compute("guid-2|cpu"));
        first.Should().MatchRegex("^HP-[0-9A-F]{20}$");
    }

    [Fact]
    public void Trial_RunsSevenDays_ThenExpires()
    {
        var started = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        var today = new DateOnly(2026, 10, 1);

        var day0 = LicenseEvaluator.Evaluate(started, today, started, null, null);
        var day6 = LicenseEvaluator.Evaluate(started.AddDays(6), today.AddDays(6), started, null, null);
        var day7 = LicenseEvaluator.Evaluate(started.AddDays(7), today.AddDays(7), started, null, null);

        day0.State.Should().Be(LicenseState.Trial);
        day0.DaysRemaining.Should().Be(7);
        day0.Terminals.Should().Be(LicenseLimits.TrialTerminals);
        day6.State.Should().Be(LicenseState.Trial);
        day6.DaysRemaining.Should().Be(1);
        day7.State.Should().Be(LicenseState.Expired);
        day7.IsExpired.Should().BeTrue();
    }

    [Fact]
    public void Licence_IsValidThroughItsExpiryDay_ThenExpired()
    {
        var licence = Payload(new DateOnly(2026, 12, 31));
        var now = new DateTime(2026, 12, 31, 12, 0, 0, DateTimeKind.Utc);

        var lastDay = LicenseEvaluator.Evaluate(now, new DateOnly(2026, 12, 31), now.AddYears(-1), licence, null);
        var nextDay = LicenseEvaluator.Evaluate(now.AddDays(1), new DateOnly(2027, 1, 1), now.AddYears(-1), licence, null);

        lastDay.State.Should().Be(LicenseState.Licensed);
        lastDay.DaysRemaining.Should().Be(0);
        lastDay.Terminals.Should().Be(5);
        nextDay.State.Should().Be(LicenseState.Expired);
    }

    [Fact]
    public void PerpetualLicence_NeverExpires()
    {
        var now = new DateTime(2040, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var evaluation = LicenseEvaluator.Evaluate(now, new DateOnly(2040, 1, 1), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), Payload(), null);

        evaluation.State.Should().Be(LicenseState.Licensed);
        evaluation.ExpiresOn.Should().BeNull();
        evaluation.DaysRemaining.Should().BeNull();
    }

    [Fact]
    public void SignUpdate_SignsTheManifestInPlace_SoServerAndTerminalsAcceptIt()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hotelpos-signupd-" + Guid.NewGuid().ToString("N"));
        var privateFile = Path.Combine(directory, "private.pem");
        var manifestFile = Path.Combine(directory, "latest.json");
        try
        {
            var keygen = new StringWriter();
            LicenseGeneratorCli.Run(new[] { "keygen", "--out", privateFile }, keygen, keygen).Should().Be(0, keygen.ToString());
            var publicKey = keygen.ToString().Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Last();
            File.WriteAllText(manifestFile, """{"version":"0.2.0","file":"HotelPOS-Client-Setup-0.2.0.exe","sha256":"abcd","size":1234}""");

            var sign = new StringWriter();
            LicenseGeneratorCli.Run(new[] { "sign-update", "--private-key", privateFile, "--manifest", manifestFile }, sign, sign).Should().Be(0, sign.ToString());

            var signed = System.Text.Json.JsonSerializer.Deserialize<HotelPOS.Contracts.Admin.ClientUpdateInfo>(
                File.ReadAllText(manifestFile), new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            signed.Version.Should().Be("0.2.0");
            signed.Size.Should().Be(1234);
            HotelPOS.Contracts.Admin.UpdateManifestSignature.Verify(signed, publicKey).Should().BeTrue();
            // Any change after signing, or another key, fails the check.
            HotelPOS.Contracts.Admin.UpdateManifestSignature.Verify(signed with { Sha256 = "abce" }, publicKey).Should().BeFalse();
            HotelPOS.Contracts.Admin.UpdateManifestSignature.Verify(signed, Pair.PublicKey).Should().BeFalse();
            HotelPOS.Contracts.Admin.UpdateManifestSignature.Verify(signed with { Signature = null }, publicKey).Should().BeFalse();

            // An incomplete manifest is refused rather than signed.
            File.WriteAllText(manifestFile, """{"version":"0.2.0","file":"x.exe"}""");
            LicenseGeneratorCli.Run(new[] { "sign-update", "--private-key", privateFile, "--manifest", manifestFile }, new StringWriter(), new StringWriter()).Should().Be(1);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void GeneratorRoundTrip_KeygenIssueInspect_ProducesAKeyTheServerAccepts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hotelpos-licgen-" + Guid.NewGuid().ToString("N"));
        var privateFile = Path.Combine(directory, "private.pem");
        try
        {
            var keygen = new StringWriter();
            LicenseGeneratorCli.Run(new[] { "keygen", "--out", privateFile }, keygen, keygen).Should().Be(0, keygen.ToString());
            var publicKey = keygen.ToString().Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Last();
            File.ReadAllText(privateFile).Should().Contain("BEGIN PRIVATE KEY");

            var issue = new StringWriter();
            LicenseGeneratorCli.Run(new[] { "issue", "--private-key", privateFile, "--machine", Machine, "--customer", "Hotel Saravana", "--terminals", "3", "--expires", "2027-03-31", "--issued", "2026-10-01" }, issue, issue)
                .Should().Be(0, issue.ToString());
            var key = issue.ToString().Trim();

            LicenseKeyCodec.TryVerify(key, publicKey, out var payload, out var error).Should().BeTrue(error);
            payload!.Terminals.Should().Be(3);
            payload.MachineId.Should().Be(Machine);
            payload.ExpiresOn.Should().Be(new DateOnly(2027, 3, 31));

            var inspect = new StringWriter();
            LicenseGeneratorCli.Run(new[] { "inspect", "--public-key", publicKey, key }, inspect, inspect).Should().Be(0);
            inspect.ToString().Should().Contain("Hotel Saravana").And.Contain("3 terminal");

            // The second keygen over the same file is refused: a private key is never overwritten.
            LicenseGeneratorCli.Run(new[] { "keygen", "--out", privateFile }, new StringWriter(), new StringWriter()).Should().Be(1);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Generator_RefusesToWriteAPrivateKeyInsideAGitRepository()
    {
        var error = new StringWriter();

        // The test runs from <repo>\tests\...\bin: inside the repository.
        var result = LicenseGeneratorCli.Run(new[] { "keygen", "--out", Path.Combine(AppContext.BaseDirectory, "private.pem") }, new StringWriter(), error);

        result.Should().Be(1);
        error.ToString().Should().Contain("git repository");
        File.Exists(Path.Combine(AppContext.BaseDirectory, "private.pem")).Should().BeFalse();
    }
}
