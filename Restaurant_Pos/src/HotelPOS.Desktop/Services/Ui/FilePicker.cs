using Microsoft.Win32;

namespace HotelPOS.Desktop.Services.Ui;

/// <summary>Opens the Windows file dialog. Behind an interface so view-models can be tested.</summary>
public interface IFilePicker
{
    /// <summary>Full path of the chosen JPEG/PNG picture, or null when cancelled.</summary>
    string? PickImage();
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
}
