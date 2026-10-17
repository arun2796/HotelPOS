using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Presentation;

namespace HotelPOS.Desktop.Modules.Common;

/// <summary>Desktop words for order states; a thin delegate to <see cref="StatusPresentation"/> (the single status map).</summary>
public static class StatusLabels
{
    public static string For(OrderStatus status) => StatusPresentation.For(status).Label;

    public static string For(TableStatus status) => StatusPresentation.For(status).Label;

    /// <summary>Resource keys of the tone brushes in Tokens.*.xaml: foreground text, soft background, solid colour.</summary>
    public static string TextBrushKey(StatusTone tone) => tone switch
    {
        StatusTone.Success => "Brush.SuccessText",
        StatusTone.Warning => "Brush.WarningText",
        StatusTone.Danger => "Brush.DangerText",
        StatusTone.Info => "Brush.InfoText",
        _ => "Brush.NeutralText",
    };

    public static string SoftBrushKey(StatusTone tone) => tone switch
    {
        StatusTone.Success => "Brush.SuccessSoft",
        StatusTone.Warning => "Brush.WarningSoft",
        StatusTone.Danger => "Brush.DangerSoft",
        StatusTone.Info => "Brush.InfoSoft",
        _ => "Brush.NeutralSoft",
    };

    public static string SolidBrushKey(StatusTone tone) => tone switch
    {
        StatusTone.Success => "Brush.Success",
        StatusTone.Warning => "Brush.Warning",
        StatusTone.Danger => "Brush.Danger",
        StatusTone.Info => "Brush.Info",
        _ => "Brush.Neutral",
    };
}
