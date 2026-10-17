using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace HotelPOS.Desktop.Shell;

public partial class ShellView : UserControl
{
    private Window? _window;

    public ShellView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private ShellViewModel? ViewModel => DataContext as ShellViewModel;

    // Shortcuts work wherever focus is in the window, so they are taken from the window's key stream.
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _window = Window.GetWindow(this);
        if (_window is not null)
        {
            _window.PreviewKeyDown += OnWindowPreviewKeyDown;
            _window.SizeChanged += OnWindowSizeChanged;
            ViewModel?.ApplyWindowWidth(_window.ActualWidth);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_window is not null)
        {
            _window.PreviewKeyDown -= OnWindowPreviewKeyDown;
            _window.SizeChanged -= OnWindowSizeChanged;
            _window = null;
        }
    }

    // Phase 21: the sidebar collapses by itself below 1440 px (see ShellViewModel.ApplyWindowWidth); in a rendering test host there
    // is no window, so nothing collapses there.
    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e) => ViewModel?.ApplyWindowWidth(e.NewSize.Width);

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is { } vm && vm.HandleShortcut(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ShellViewModel old)
        {
            old.Palette.PropertyChanged -= OnPaletteChanged;
            old.PropertyChanged -= OnShellChanged;
        }

        if (e.NewValue is ShellViewModel vm)
        {
            vm.Palette.PropertyChanged += OnPaletteChanged;
            vm.PropertyChanged += OnShellChanged;
        }
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ShellViewModel.IsSidebarCollapsed) || !IsLoaded || !Controls.TransitioningContentControl.AnimationsAllowed)
        {
            return;
        }

        var from = Sidebar.ActualWidth;
        var to = ViewModel?.IsSidebarCollapsed == true ? 64 : 240;
        Sidebar.BeginAnimation(WidthProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        });
    }

    private void OnPaletteChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CommandPaletteViewModel.IsOpen) && sender is CommandPaletteViewModel { IsOpen: true })
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => PaletteQuery.Focus());
        }

        if (e.PropertyName == nameof(CommandPaletteViewModel.Selected) && sender is CommandPaletteViewModel { Selected: { } selected })
        {
            PaletteResults.ScrollIntoView(selected);
        }
    }

    private void OnPaletteKeyDown(object sender, KeyEventArgs e)
    {
        var palette = ViewModel?.Palette;
        if (palette is null)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                palette.MoveNextCommand.Execute(null);
                break;
            case Key.Up:
                palette.MovePreviousCommand.Execute(null);
                break;
            case Key.Enter:
                _ = palette.ExecuteCommand.ExecuteAsync(null);
                break;
            case Key.Escape:
                palette.CloseCommand.Execute(null);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnPaletteResultClicked(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is PaletteEntry entry && ViewModel is { } vm)
        {
            _ = vm.Palette.ExecuteCommand.ExecuteAsync(entry);
        }
    }

    private void OnPaletteScrimClicked(object sender, MouseButtonEventArgs e) => ViewModel?.Palette.CloseCommand.Execute(null);

    // Clicks inside the palette must not reach the scrim behind it.
    private void OnPaletteClicked(object sender, MouseButtonEventArgs e) => e.Handled = true;
}
