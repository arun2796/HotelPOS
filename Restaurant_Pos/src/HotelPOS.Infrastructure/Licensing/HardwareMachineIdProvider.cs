using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Domain.Licensing;
using Microsoft.Win32;

namespace HotelPOS.Infrastructure.Licensing;

// The server's identity for licensing: the OS install's machine GUID plus the processor model, hashed (MachineIds).
// Renaming the PC or changing its IP does not change it; reinstalling Windows or swapping the CPU does.
public sealed class HardwareMachineIdProvider : IMachineIdProvider
{
    private readonly Lazy<string> _id = new(Compute);

    public string GetMachineId() => _id.Value;

    private static string Compute()
    {
        string? guid = null;
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            guid = key?.GetValue("MachineGuid") as string;
        }
        else if (File.Exists("/etc/machine-id"))
        {
            guid = File.ReadAllText("/etc/machine-id").Trim();
        }

        var processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? string.Empty;
        return MachineIds.Compute($"{guid ?? Environment.MachineName}|{processor}");
    }
}
