using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace HotelPOS.Desktop.Controls;

// Content host for screens and pages: new content fades in while sliding up 8 px (160 ms), as in BillingHub.
// Skipped when Windows animation effects are off, under software rendering (remote sessions, weak tablets) and
// before the control is loaded, so the first screen and off-screen rendering appear at once.
public sealed class TransitioningContentControl : ContentControl
{
    private static readonly Duration Length = new(TimeSpan.FromMilliseconds(160));
    private const double Offset = 8;

    public TransitioningContentControl()
    {
        Focusable = false;
    }

    public static bool AnimationsAllowed =>
        SystemParameters.ClientAreaAnimation && RenderCapability.Tier >> 16 >= 1;

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        if (oldContent is null || newContent is null || !IsLoaded || !AnimationsAllowed)
        {
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var slide = new TranslateTransform(0, Offset);
        RenderTransform = slide;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Length) { EasingFunction = ease });
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(Offset, 0, Length) { EasingFunction = ease });
    }
}
