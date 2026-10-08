using Microsoft.Win32;

namespace HotelPOS.Desktop.Services.Ui;

public interface IFilePicker
{
    string? PickImage();

    string? PickSavePath(string suggestedFileName, string filter);
}

public sealed class WpfFilePicker : IFilePicker
{
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
