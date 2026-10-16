using Microsoft.Win32;

namespace HotelPOS.Desktop.Services.Ui;

public interface IFilePicker
{
    string? PickImage();

    string? PickSavePath(string suggestedFileName, string filter);

    string? PickImportFile();

    // Opens a file the app just saved (export, PDF) in whatever Windows associates with it.
    void Open(string path);
}

public sealed class WpfFilePicker : IFilePicker
{
    public void Open(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or System.IO.IOException)
        {
            // No associated program, or the file is gone: nothing to do beyond not crashing the app.
        }
    }

    public string? PickImage()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a picture",
            Filter = "Pictures (*.jpg; *.jpeg; *.png)|*.jpg;*.jpeg;*.png",
            CheckFileExists = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickImportFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose the file to import",
            Filter = "Excel or CSV (*.xlsx; *.csv)|*.xlsx;*.csv",
            CheckFileExists = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickSavePath(string suggestedFileName, string filter)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save as",
            FileName = suggestedFileName,
            Filter = filter,
            AddExtension = true,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
