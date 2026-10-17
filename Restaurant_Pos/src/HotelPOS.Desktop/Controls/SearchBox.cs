using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HotelPOS.Desktop.Controls;

// Search field: icon, placeholder text and an optional shortcut hint ("Ctrl K"). Used as a normal text field (bind Text),
// or as the shell's launcher for the command palette: IsReadOnly="True" plus Command — a click or Enter runs the command.
public sealed class SearchBox : TextBox
{
    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(SearchBox), new PropertyMetadata("Search"));

    public static readonly DependencyProperty ShortcutHintProperty =
        DependencyProperty.Register(nameof(ShortcutHint), typeof(string), typeof(SearchBox), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(SearchBox), new PropertyMetadata(null));

    static SearchBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SearchBox), new FrameworkPropertyMetadata(typeof(SearchBox)));
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public string ShortcutHint
    {
        get => (string)GetValue(ShortcutHintProperty);
        set => SetValue(ShortcutHintProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (IsReadOnly && Command is not null)
        {
            e.Handled = true;
            Run();
            return;
        }

        base.OnPreviewMouseLeftButtonDown(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (IsReadOnly && Command is not null && e.Key is Key.Enter or Key.Space)
        {
            e.Handled = true;
            Run();
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    private void Run()
    {
        if (Command is { } command && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }
}
