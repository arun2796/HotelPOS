using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Desktop.Services.Configuration;

/// <summary>Stores small secrets (the refresh token) encrypted for the current Windows user.</summary>
public interface ISecureStore
{
    void Save(string name, string value);

    string? Load(string name);

    void Delete(string name);
}

/// <summary>Windows DPAPI (CurrentUser scope): the file is useless on another account or PC.</summary>
public sealed class DpapiSecureStore : ISecureStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("HotelPOS.Desktop.v1");

    private readonly AppPaths _paths;
    private readonly ILogger<DpapiSecureStore> _logger;

    public DpapiSecureStore(AppPaths paths, ILogger<DpapiSecureStore> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public void Save(string name, string value)
    {
        try
        {
            var path = PathFor(name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(path, protectedBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            _logger.LogWarning(ex, "Could not persist secure item {Name}", name);
        }
    }

    public string? Load(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            _logger.LogWarning(ex, "Could not read secure item {Name}; discarding it", name);
            Delete(name);
            return null;
        }
    }

    public void Delete(string name)
    {
        try
        {
            File.Delete(PathFor(name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not delete secure item {Name}", name);
        }
    }

    private string PathFor(string name) => Path.Combine(_paths.UserDataDirectory, name + ".dat");
}
