using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace HotelPOS.Desktop.Modules.Admin;

public partial class FloorEditorView : UserControl
{
    private Point _dragStart;
    private int _startX;
    private int _startY;
    private int _startWidth;
    private int _startHeight;
    private bool _moving;
    private double _handleDx;
    private double _handleDy;

    public FloorEditorView()
    {
        InitializeComponent();
    }

    private FloorEditorViewModel? Vm => DataContext as FloorEditorViewModel;

    private static FloorEditorItem? ItemOf(object sender) => (sender as FrameworkElement)?.DataContext as FloorEditorItem;

    private static bool IsFromThumb(object? source)
    {
        var node = source as DependencyObject;
        while (node is not null)
        {
            if (node is Thumb)
            {
                return true;
            }

            node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }

        return false;
    }

    private void OnFitClick(object sender, RoutedEventArgs e) => Vm?.FitToWidth(Viewport.ViewportWidth - 4);

    private void OnSurfaceMouseDown(object sender, MouseButtonEventArgs e)
    {
        Surface.Focus();
        if (ReferenceEquals(e.OriginalSource, Surface) || e.OriginalSource is Canvas)
        {
            Vm?.Select(null);
        }
    }

    private void OnTileMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ItemOf(sender) is not { } item || Vm is not { } vm || IsFromThumb(e.OriginalSource))
        {
            return;
        }

        vm.Select(item);
        vm.BeginGesture();
        _moving = true;
        _dragStart = e.GetPosition(Surface);
        _startX = item.X;
        _startY = item.Y;
        ((UIElement)sender).CaptureMouse();
        Surface.Focus();
        e.Handled = true;
    }

    private void OnTileMouseMove(object sender, MouseEventArgs e)
    {
        if (!_moving || ItemOf(sender) is not { } item || Vm is not { } vm)
        {
            return;
        }

        var position = e.GetPosition(Surface);
        vm.MoveTo(item, _startX + (int)Math.Round(position.X - _dragStart.X), _startY + (int)Math.Round(position.Y - _dragStart.Y));
    }

    private void OnTileMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_moving)
        {
            return;
        }

        _moving = false;
        ((UIElement)sender).ReleaseMouseCapture();
        Vm?.EndGesture();
    }

    private void OnHandleDragStarted(object sender, DragStartedEventArgs e)
    {
        if (ItemOf(sender) is not { } item || Vm is not { } vm)
        {
            return;
        }

        vm.BeginGesture();
        _startX = item.X;
        _startY = item.Y;
        _startWidth = item.Width;
        _startHeight = item.Height;
        _handleDx = 0;
        _handleDy = 0;
    }

    private void OnHandleDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (ItemOf(sender) is not { } item || Vm is not { } vm || e.OriginalSource is not Thumb { Tag: string handle })
        {
            return;
        }

        // The thumb's coordinates rotate with the tile, so the offsets are already in the tile's own frame.
        _handleDx += e.HorizontalChange;
        _handleDy += e.VerticalChange;
        vm.ResizeFromHandle(item, handle, _startX, _startY, _startWidth, _startHeight, _handleDx, _handleDy);
    }

    private void OnHandleDragCompleted(object sender, DragCompletedEventArgs e) => Vm?.EndGesture();

    private void OnSurfaceKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is not { } vm)
        {
            return;
        }

        var step = FloorEditorViewModel.GridSize;
        switch (e.Key)
        {
            case Key.Left: vm.Nudge(-step, 0); break;
            case Key.Right: vm.Nudge(step, 0); break;
            case Key.Up: vm.Nudge(0, -step); break;
            case Key.Down: vm.Nudge(0, step); break;
            case Key.Z when Keyboard.Modifiers == ModifierKeys.Control: vm.UndoCommand.Execute(null); break;
            case Key.Y when Keyboard.Modifiers == ModifierKeys.Control: vm.RedoCommand.Execute(null); break;
            default: return;
        }

        e.Handled = true;
    }
}
