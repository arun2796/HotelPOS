namespace HotelPOS.Application.Common.Interfaces;

public interface IMenuVersionStore
{
    Task<int> GetAsync(CancellationToken cancellationToken = default);

    Task<int> BumpAsync(CancellationToken cancellationToken = default);
}

public interface IMenuImageStore
{
    Task<string?> SaveAsync(int menuItemId, Stream content, CancellationToken cancellationToken = default);

    void Delete(string fileName);
}
