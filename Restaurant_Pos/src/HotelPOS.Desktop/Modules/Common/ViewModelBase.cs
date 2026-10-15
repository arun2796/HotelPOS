using CommunityToolkit.Mvvm.ComponentModel;
using HotelPOS.Desktop.Services.Api;
using Serilog;

namespace HotelPOS.Desktop.Modules.Common;

public abstract partial class ViewModelBase : ObservableObject
{
    public const string UnexpectedErrorMessage = "Something went wrong. The problem was logged; please try again.";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    partial void OnIsBusyChanged(bool value) => OnBusyChanged();

    protected virtual void OnBusyChanged()
    {
    }

    protected async Task ExecuteAsync(Func<Task> operation)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await operation();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A fault stays on the screen that hit it: the user sees why there, and the terminal keeps working.
            Log.Error(ex, "Unexpected error in {ViewModel}", GetType().Name);
            ErrorMessage = UnexpectedErrorMessage;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Loads change nothing on the server, so a connection failure is reported as the client describes it and
    // never with the "was NOT saved" wording of a failed save.
    protected async Task<T?> FetchAsync<T>(Task<ApiResult<T>> call)
        where T : class
    {
        var result = await call;
        if (result.Success && result.Data is not null)
        {
            return result.Data;
        }

        ErrorMessage = result.IsConnectionFailure ? result.Message : ApiFailures.Describe(result);
        return null;
    }
}
