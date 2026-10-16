using System.Media;
using HotelPOS.Contracts.Realtime;
using Microsoft.Extensions.Logging;

namespace HotelPOS.Desktop.Services.Ui;

public interface ISoundPlayer
{
    // Plays the cue for one kind of notification. Whether a kind may make a sound is decided by the caller
    // (NotificationRouter, from the per-terminal preferences); this only makes the noise.
    void Play(NotificationKind kind);
}

public sealed class SystemSoundPlayer : ISoundPlayer
{
    private readonly ILogger<SystemSoundPlayer> _logger;

    public SystemSoundPlayer(ILogger<SystemSoundPlayer> logger)
    {
        _logger = logger;
    }

    // Terminals without an audio device must keep working silently.
    public void Play(NotificationKind kind)
    {
        try
        {
            var sound = kind switch
            {
                NotificationKind.PrintFailed or NotificationKind.PaymentFailed => SystemSounds.Hand,
                NotificationKind.TicketDelayed or NotificationKind.DayEndWarning => SystemSounds.Beep,
                _ => SystemSounds.Exclamation,
            };
            sound.Play();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No sound for {Kind}", kind);
        }
    }
}
