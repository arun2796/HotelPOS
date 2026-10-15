using System.Media;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Desktop.Services.Ui;

public interface ISoundPlayer
{
    void NewTicket();
}

public sealed class SystemSoundPlayer : ISoundPlayer
{
    private readonly ILogger<SystemSoundPlayer> _logger;

    public SystemSoundPlayer(ILogger<SystemSoundPlayer> logger)
    {
        _logger = logger;
    }

    // Kitchen PCs without an audio device must keep working silently.
    public void NewTicket()
    {
        try
        {
            SystemSounds.Exclamation.Play();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No sound for the new ticket");
        }
    }
}
