namespace HotelPOS.Application.Common.Interfaces;

/// <summary>The menu version counter (setting <c>MenuVersion</c>). Clients compare it to their cached copy.</summary>
public interface IMenuVersionStore
{
    Task<int> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically increments the version and returns the new value. Runs on the caller's connection, so it
    /// commits or rolls back with the caller's transaction.
    /// </summary>
    Task<int> BumpAsync(CancellationToken cancellationToken = default);
}

/// <summary>Stores menu item pictures, resized for terminals.</summary>
public interface IMenuImageStore
{
    /// <summary>
    /// Validates (JPEG or PNG), resizes and stores the picture. Returns the stored file name, or null when the
    /// content is not a readable JPEG/PNG image.
    /// </summary>
    Task<string?> SaveAsync(int menuItemId, Stream content, CancellationToken cancellationToken = default);

    /// <summary>Removes a stored picture; missing files are ignored.</summary>
    void Delete(string fileName);
}
