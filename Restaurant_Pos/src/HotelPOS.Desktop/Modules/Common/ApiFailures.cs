using HotelPOS.Desktop.Services.Api;

namespace HotelPOS.Desktop.Modules.Common;

public static class ApiFailures
{
    public static string Describe<T>(ApiResult<T> result)
    {
        if (result.IsConnectionFailure)
        {
            return "Connection unavailable. The change was NOT saved.";
        }

        var fieldErrors = result.Errors.Where(e => e.Field is not null).Select(e => e.Message).ToList();
        return fieldErrors.Count > 0 ? string.Join("\n", fieldErrors) : result.Message;
    }
}
