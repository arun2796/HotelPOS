using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HotelPOS.Desktop.Controls;

// One line of an order or bill: picture (or the item's initials when it has none), "2 × Masala dosa", modifiers in
// secondary text, a note in a warning callout with an icon, the price on the right. Controls such as a quantity stepper go
// in Content, under the text. Used by the cashier workspace, the kitchen detail pane and the order builder.
public sealed class OrderLine : ContentControl
{
    public static readonly DependencyProperty QuantityProperty =
        DependencyProperty.Register(nameof(Quantity), typeof(int), typeof(OrderLine), new PropertyMetadata(1, OnTitleChanged));

    public static readonly DependencyProperty ItemNameProperty =
        DependencyProperty.Register(nameof(ItemName), typeof(string), typeof(OrderLine), new PropertyMetadata(string.Empty, OnNameChanged));

    public static readonly DependencyProperty ModifiersProperty =
        DependencyProperty.Register(nameof(Modifiers), typeof(string), typeof(OrderLine), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty NoteProperty =
        DependencyProperty.Register(nameof(Note), typeof(string), typeof(OrderLine), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty PriceTextProperty =
        DependencyProperty.Register(nameof(PriceText), typeof(string), typeof(OrderLine), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ImageProperty =
        DependencyProperty.Register(nameof(Image), typeof(ImageSource), typeof(OrderLine), new PropertyMetadata(null));

    public static readonly DependencyProperty ImageSizeProperty =
        DependencyProperty.Register(nameof(ImageSize), typeof(double), typeof(OrderLine), new PropertyMetadata(48.0));

    private static readonly DependencyPropertyKey TitlePropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(Title), typeof(string), typeof(OrderLine), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TitleProperty = TitlePropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey InitialsPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(Initials), typeof(string), typeof(OrderLine), new PropertyMetadata("?"));

    public static readonly DependencyProperty InitialsProperty = InitialsPropertyKey.DependencyProperty;

    static OrderLine()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(OrderLine), new FrameworkPropertyMetadata(typeof(OrderLine)));
        FocusableProperty.OverrideMetadata(typeof(OrderLine), new FrameworkPropertyMetadata(false));
    }

    public int Quantity
    {
        get => (int)GetValue(QuantityProperty);
        set => SetValue(QuantityProperty, value);
    }

    public string ItemName
    {
        get => (string)GetValue(ItemNameProperty);
        set => SetValue(ItemNameProperty, value);
    }

    public string Modifiers
    {
        get => (string)GetValue(ModifiersProperty);
        set => SetValue(ModifiersProperty, value);
    }

    public string Note
    {
        get => (string)GetValue(NoteProperty);
        set => SetValue(NoteProperty, value);
    }

    public string PriceText
    {
        get => (string)GetValue(PriceTextProperty);
        set => SetValue(PriceTextProperty, value);
    }

    public ImageSource? Image
    {
        get => (ImageSource?)GetValue(ImageProperty);
        set => SetValue(ImageProperty, value);
    }

    /// <summary>Picture edge in px: 48 in a list, up to 56 in the cashier workspace.</summary>
    public double ImageSize
    {
        get => (double)GetValue(ImageSizeProperty);
        set => SetValue(ImageSizeProperty, value);
    }

    /// <summary>"2 × Masala dosa".</summary>
    public string Title => (string)GetValue(TitleProperty);

    public string Initials => (string)GetValue(InitialsProperty);

    public static string InitialsOf(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => char.IsLetterOrDigit(w[0]))
            .Take(2)
            .Select(w => char.ToUpperInvariant(w[0]))
            .ToArray();
        return words.Length == 0 ? "?" : new string(words);
    }

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var line = (OrderLine)d;
        line.SetValue(TitlePropertyKey, $"{line.Quantity} × {line.ItemName}");
    }

    private static void OnNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var line = (OrderLine)d;
        line.SetValue(InitialsPropertyKey, InitialsOf((string)e.NewValue));
        OnTitleChanged(d, e);
    }
}
