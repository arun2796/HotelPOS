using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HotelPOS.Desktop.Controls;

// Right-hand drawer next to a list (kitchen order detail, cashier order, table details): a header with a title,
// an optional subtitle and a close button, then the content. Put it in the right column; PaneWidth sets its width.
public sealed class DetailPane : ContentControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(DetailPane), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(DetailPane), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CloseCommandProperty =
        DependencyProperty.Register(nameof(CloseCommand), typeof(ICommand), typeof(DetailPane), new PropertyMetadata(null));

    public static readonly DependencyProperty PaneWidthProperty =
        DependencyProperty.Register(nameof(PaneWidth), typeof(double), typeof(DetailPane), new PropertyMetadata(380.0));

    static DetailPane()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(DetailPane), new FrameworkPropertyMetadata(typeof(DetailPane)));
        FocusableProperty.OverrideMetadata(typeof(DetailPane), new FrameworkPropertyMetadata(false));
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public ICommand? CloseCommand
    {
        get => (ICommand?)GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public double PaneWidth
    {
        get => (double)GetValue(PaneWidthProperty);
        set => SetValue(PaneWidthProperty, value);
    }
}

// A failed load shown in place of the data: danger banner with an icon, a title, one sentence and a Retry button
// (hidden while RetryCommand is null). Extra content (e.g. a reference id) goes in Content.
public sealed class ErrorState : ContentControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(ErrorState), new PropertyMetadata("Something went wrong"));

    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(ErrorState), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(ErrorState), new PropertyMetadata("alert"));

    public static readonly DependencyProperty RetryCommandProperty =
        DependencyProperty.Register(nameof(RetryCommand), typeof(ICommand), typeof(ErrorState), new PropertyMetadata(null));

    public static readonly DependencyProperty RetryTextProperty =
        DependencyProperty.Register(nameof(RetryText), typeof(string), typeof(ErrorState), new PropertyMetadata("Retry"));

    static ErrorState()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ErrorState), new FrameworkPropertyMetadata(typeof(ErrorState)));
        FocusableProperty.OverrideMetadata(typeof(ErrorState), new FrameworkPropertyMetadata(false));
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public ICommand? RetryCommand
    {
        get => (ICommand?)GetValue(RetryCommandProperty);
        set => SetValue(RetryCommandProperty, value);
    }

    public string RetryText
    {
        get => (string)GetValue(RetryTextProperty);
        set => SetValue(RetryTextProperty, value);
    }
}
