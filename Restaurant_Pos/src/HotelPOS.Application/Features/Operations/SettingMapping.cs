using HotelPOS.Contracts.Admin;
using HotelPOS.Domain.Administration;

namespace HotelPOS.Application.Features.Operations;

internal static class SettingMapping
{
    public static SettingDto ToDto(Setting s) => new()
    {
        Key = s.Key,
        Value = s.Value,
        DataType = s.DataType,
        Description = s.Description,
        IsPublic = s.IsPublic,
    };
}
