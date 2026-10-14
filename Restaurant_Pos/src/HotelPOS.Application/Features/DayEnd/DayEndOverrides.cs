using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Cash;

namespace HotelPOS.Application.Features.DayEnd;

// Each blocking item needs an explicit override with a reason; the overrides are stored with the close and audited.
internal static class DayEndOverrides
{
    public static AppError? Missing(IReadOnlyList<DayEndCheckItemDto> blocking, IReadOnlyList<DayEndOverrideDto> overrides)
    {
        var uncovered = blocking.Where(b => Reason(overrides, b.Code) is null).ToList();
        return uncovered.Count == 0
            ? null
            : AppErrors.BusinessRule(
                $"Day End is blocked by {string.Join(", ", uncovered.Select(b => $"{b.Count} {b.Title.ToLowerInvariant()}"))}. "
                + "Settle them, or override each with a reason.") with { Data = blocking };
    }

    public static IReadOnlyList<DayEndOverrideRecordDto> Records(IReadOnlyList<DayEndCheckItemDto> blocking, IReadOnlyList<DayEndOverrideDto> overrides) =>
        blocking.Select(b => new DayEndOverrideRecordDto { Code = b.Code, Count = b.Count, Reason = Reason(overrides, b.Code) ?? string.Empty }).ToList();

    private static string? Reason(IReadOnlyList<DayEndOverrideDto> overrides, string code) =>
        overrides.FirstOrDefault(o => string.Equals(o.Code, code, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(o.Reason))?.Reason.Trim();
}
