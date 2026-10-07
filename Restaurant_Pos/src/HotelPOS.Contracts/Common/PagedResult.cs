namespace HotelPOS.Contracts.Common;

public sealed record PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public record PagedQuery
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = DefaultPageSize;
}
