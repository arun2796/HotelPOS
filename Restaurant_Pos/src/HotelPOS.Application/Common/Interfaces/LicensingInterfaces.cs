using HotelPOS.Contracts.Admin;

namespace HotelPOS.Application.Common.Interfaces;

// The stable hardware identity of the server PC, as MachineIds.Compute turns it into an id.
public interface IMachineIdProvider
{
    string GetMachineId();
}

// The vendor's public key (SubjectPublicKeyInfo, base64). The tests swap it for their own pair.
public interface ILicenseTrust
{
    string PublicKey { get; }
}

public sealed record TabularData(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows);

public sealed class TabularFormatException : Exception
{
    public TabularFormatException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

// Reads the first sheet of an .xlsx file or a .csv file into text cells; the first row holds the headers.
public interface ITabularReader
{
    TabularData Read(Stream content, string fileName);
}

// The newest terminal installer on this server (/updates/latest.json), or null.
public interface IClientUpdateSource
{
    ClientUpdateInfo? Latest();
}
