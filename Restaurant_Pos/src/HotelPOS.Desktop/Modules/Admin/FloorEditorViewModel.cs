using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Services.Api;
using HotelPOS.Desktop.Services.Navigation;
using HotelPOS.Desktop.Services.Realtime;
using HotelPOS.Desktop.Services.Ui;

namespace HotelPOS.Desktop.Modules.Admin;

/// <summary>One table on the floor-plan editor canvas (layout units = px at 100 % zoom).</summary>
public sealed partial class FloorEditorItem : ObservableObject
{
    public FloorEditorItem(FloorTableDto table)
    {
        TableId = table.TableId;
        Code = table.Code;
        Capacity = table.Capacity;
        Status = table.Status;
        IsPlaced = table.HasLayout && table.PosX is not null;
        X = table.PosX ?? 0;
        Y = table.PosY ?? 0;
        Width = table.Width ?? FloorEditorViewModel.DefaultWidth;
        Height = table.Height ?? FloorEditorViewModel.DefaultHeight;
        Shape = table.Shape ?? TableShape.Rectangle;
        Rotation = table.Rotation ?? 0;
    }

    public int TableId { get; }

    public string Code { get; }

    public int Capacity { get; }

    public TableStatus Status { get; }

    public string SeatsText => $"{Capacity} seats";

    [ObservableProperty]
    private bool _isPlaced;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private int _x;

    [ObservableProperty]
    private int _y;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CornerRadiusValue))]
    private int _width;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CornerRadiusValue))]
    private int _height;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CornerRadiusValue), nameof(IsRound))]
    private TableShape _shape;

    [ObservableProperty]
    private int _rotation;

    public bool IsRound => Shape == TableShape.Round;

    public CornerRadius CornerRadiusValue => new(Shape == TableShape.Round ? Math.Min(Width, Height) / 2.0 : 8);
}

public sealed partial class FloorEditorViewModel : ObservableObject, INavigationAware, IRefreshable, IDisposable
{
    public const int GridSize = 8;
    public const int DefaultWidth = 120;
    public const int DefaultHeight = 80;
    public const int GridGap = 24;
    public const int GridColumns = 6;
    public const int CanvasMinWidth = 1200;
    public const int CanvasMinHeight = 800;
    public const double MinZoom = 0.25;
    public const double MaxZoom = 2.0;

    private readonly IFloorLayoutApi _layoutApi;
    private readonly IFloorApi _floorApi;
    private readonly IRealtimeClient _realtime;
    private readonly INotificationService _notifications;
    private readonly List<IDisposable> _subscriptions = new();
    private readonly Stack<Snap[]> _undo = new();
    private readonly Stack<Snap[]> _redo = new();
    private Snap[] _baseline = Array.Empty<Snap>();
    private Snap[]? _gesture;
    private bool _loading;

    public FloorEditorViewModel(IFloorLayoutApi layoutApi, IFloorApi floorApi, IRealtimeClient realtime, INotificationService notifications)
    {
        _layoutApi = layoutApi;
        _floorApi = floorApi;
        _realtime = realtime;
        _notifications = notifications;
    }

    private readonly record struct Snap(int TableId, bool Placed, int X, int Y, int Width, int Height, TableShape Shape, int Rotation);

    public ObservableCollection<SectionDto> Sections { get; } = new();

    public ObservableCollection<FloorEditorItem> Items { get; } = new();

    public ObservableCollection<FloorEditorItem> Placed { get; } = new();

    public ObservableCollection<FloorEditorItem> Unplaced { get; } = new();

    [ObservableProperty]
    private SectionDto? _selectedSection;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RotateCommand), nameof(ToggleShapeCommand))]
    private FloorEditorItem? _selectedItem;

    [ObservableProperty]
    private bool _snapEnabled = true;

    [ObservableProperty]
    private double _zoom = 1.0;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DirtyText))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isDirty;

    public string DirtyText => IsDirty ? "Unsaved changes" : "All changes saved";

    public bool HasUnplaced => Unplaced.Count > 0;

    public int CanvasWidth => Math.Max(CanvasMinWidth, Placed.Count == 0 ? 0 : Placed.Max(i => i.X + i.Width) + GridGap);

    public int CanvasHeight => Math.Max(CanvasMinHeight, Placed.Count == 0 ? 0 : Placed.Max(i => i.Y + i.Height) + GridGap);

    public async Task OnNavigatedToAsync(object? parameter)
    {
        _subscriptions.Add(_realtime.Subscribe<FloorLayoutChangedEvent>(HubEvents.FloorLayoutChanged, OnLayoutChanged));
        await RefreshAsync();
    }

    public void OnNavigatedFrom() => Dispose();

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }

    public async Task RefreshAsync()
    {
        if (Sections.Count == 0 || SelectedSection is null)
        {
            var sections = await _floorApi.GetSectionsAsync();
            if (!sections.Success || sections.Data is null)
            {
                ErrorMessage = sections.IsConnectionFailure ? sections.Message : ApiFailures.Describe(sections);
                return;
            }

            var keep = SelectedSection?.Id;
            _loading = true;
            Sections.Clear();
            foreach (var section in sections.Data.OrderBy(s => s.SortOrder))
            {
                Sections.Add(section);
            }

            SelectedSection = Sections.FirstOrDefault(s => s.Id == keep) ?? Sections.FirstOrDefault();
            _loading = false;
        }

        await LoadLayoutAsync();
    }

    partial void OnSelectedSectionChanged(SectionDto? value)
    {
        if (!_loading)
        {
            _ = LoadLayoutAsync();
        }
    }

    partial void OnSelectedItemChanged(FloorEditorItem? oldValue, FloorEditorItem? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }
    }

    partial void OnZoomChanged(double value)
    {
        var clamped = Math.Clamp(value, MinZoom, MaxZoom);
        if (Math.Abs(clamped - value) > 0.0001)
        {
            Zoom = clamped;
        }
    }

    public async Task LoadLayoutAsync()
    {
        if (SelectedSection is not { } section)
        {
            ApplyLayout(null);
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _layoutApi.GetAsync(section.Id);
            if (!result.Success || result.Data is null)
            {
                ErrorMessage = result.IsConnectionFailure ? result.Message : ApiFailures.Describe(result);
                return;
            }

            ApplyLayout(result.Data);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void ApplyLayout(FloorLayoutDto? layout)
    {
        SelectedItem = null;
        Items.Clear();
        foreach (var table in (layout?.Tables ?? Array.Empty<FloorTableDto>()).OrderBy(t => t.Code, StringComparer.OrdinalIgnoreCase))
        {
            Items.Add(new FloorEditorItem(table));
        }

        _undo.Clear();
        _redo.Clear();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        _baseline = Capture();
        Rebuild();
        IsDirty = false;
    }

    public static int Clamp(int value, int min, int max) => Math.Min(max, Math.Max(min, value));

    public static int SnapToGrid(int value) => (int)(Math.Round(value / (double)GridSize, MidpointRounding.AwayFromZero) * GridSize);

    private int Normalise(int value, int min, int max) => Clamp(SnapEnabled ? SnapToGrid(value) : value, min, max);

    // ---- gestures (a drag or a resize records one undo step) ----

    public void BeginGesture() => _gesture ??= Capture();

    public void EndGesture()
    {
        if (_gesture is { } before && !before.SequenceEqual(Capture()))
        {
            PushUndo(before);
        }

        _gesture = null;
        Rebuild();
    }

    public void Select(FloorEditorItem? item) => SelectedItem = item;

    /// <summary>Move a placed table (snapped and clamped); outside a gesture this is one undo step.</summary>
    public void MoveTo(FloorEditorItem item, int x, int y)
    {
        var before = _gesture is null ? Capture() : null;
        item.X = Normalise(x, FloorLayoutLimits.MinCoordinate, FloorLayoutLimits.MaxCoordinate);
        item.Y = Normalise(y, FloorLayoutLimits.MinCoordinate, FloorLayoutLimits.MaxCoordinate);
        Commit(before);
    }

    public void ResizeTo(FloorEditorItem item, int x, int y, int width, int height)
    {
        var before = _gesture is null ? Capture() : null;
        item.Width = Normalise(width, FloorLayoutLimits.MinSize, FloorLayoutLimits.MaxSize);
        item.Height = Normalise(height, FloorLayoutLimits.MinSize, FloorLayoutLimits.MaxSize);
        item.X = Normalise(x, FloorLayoutLimits.MinCoordinate, FloorLayoutLimits.MaxCoordinate);
        item.Y = Normalise(y, FloorLayoutLimits.MinCoordinate, FloorLayoutLimits.MaxCoordinate);
        Commit(before);
    }

    /// <summary>Resize from a handle ("N", "NE", "E", ...) given the geometry at the start of the drag and the total pointer offset.</summary>
    public void ResizeFromHandle(FloorEditorItem item, string handle, int startX, int startY, int startWidth, int startHeight, double dx, double dy)
    {
        int x = startX, y = startY, w = startWidth, h = startHeight;
        if (handle.Contains('E'))
        {
            w = startWidth + (int)Math.Round(dx);
        }

        if (handle.Contains('W'))
        {
            w = startWidth - (int)Math.Round(dx);
            x = startX + startWidth - w;
        }

        if (handle.Contains('S'))
        {
            h = startHeight + (int)Math.Round(dy);
        }

        if (handle.Contains('N'))
        {
            h = startHeight - (int)Math.Round(dy);
            y = startY + startHeight - h;
        }

        ResizeTo(item, x, y, w, h);
    }

    public void Nudge(int dx, int dy)
    {
        if (SelectedItem is { IsPlaced: true } item)
        {
            var before = Capture();
            item.X = Clamp(item.X + dx, FloorLayoutLimits.MinCoordinate, FloorLayoutLimits.MaxCoordinate);
            item.Y = Clamp(item.Y + dy, FloorLayoutLimits.MinCoordinate, FloorLayoutLimits.MaxCoordinate);
            Commit(before);
        }
    }

    public void FitToWidth(double viewportWidth)
    {
        if (viewportWidth > 0)
        {
            Zoom = Math.Clamp(viewportWidth / CanvasWidth, MinZoom, MaxZoom);
        }
    }

    // ---- commands ----

    private bool HasSelection() => SelectedItem is { IsPlaced: true };

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Rotate()
    {
        if (SelectedItem is { } item)
        {
            var before = Capture();
            item.Rotation = (item.Rotation + 90) % 360;
            Commit(before);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ToggleShape()
    {
        if (SelectedItem is { } item)
        {
            var before = Capture();
            item.Shape = item.Shape == TableShape.Round ? TableShape.Rectangle : TableShape.Round;
            Commit(before);
        }
    }

    [RelayCommand]
    private void Place(FloorEditorItem? item)
    {
        if (item is null || item.IsPlaced)
        {
            return;
        }

        var before = Capture();
        var (x, y) = NextFreeSlot();
        item.X = x;
        item.Y = y;
        item.Width = DefaultWidth;
        item.Height = DefaultHeight;
        item.Shape = TableShape.Rectangle;
        item.Rotation = 0;
        item.IsPlaced = true;
        Commit(before);
        SelectedItem = item;
        RotateCommand.NotifyCanExecuteChanged();
        ToggleShapeCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ResetToGrid()
    {
        var before = Capture();
        var index = 0;
        foreach (var item in Items)
        {
            (item.X, item.Y) = GridSlot(index++);
            item.Width = DefaultWidth;
            item.Height = DefaultHeight;
            item.Shape = TableShape.Rectangle;
            item.Rotation = 0;
            item.IsPlaced = true;
        }

        Commit(before);
        RotateCommand.NotifyCanExecuteChanged();
        ToggleShapeCommand.NotifyCanExecuteChanged();
    }

    private bool CanUndo() => _undo.Count > 0;

    private bool CanRedo() => _redo.Count > 0;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _redo.Push(Capture());
        Restore(_undo.Pop());
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _undo.Push(Capture());
        Restore(_redo.Pop());
    }

    public SaveFloorLayoutRequest BuildRequest() => new()
    {
        Items = Items.Where(i => i.IsPlaced)
            .Select(i => new FloorLayoutItem
            {
                TableId = i.TableId,
                PosX = Clamp(i.X, FloorLayoutLimits.MinCoordinate, FloorLayoutLimits.MaxCoordinate),
                PosY = Clamp(i.Y, FloorLayoutLimits.MinCoordinate, FloorLayoutLimits.MaxCoordinate),
                Width = Clamp(i.Width, FloorLayoutLimits.MinSize, FloorLayoutLimits.MaxSize),
                Height = Clamp(i.Height, FloorLayoutLimits.MinSize, FloorLayoutLimits.MaxSize),
                Shape = i.Shape,
                Rotation = ((i.Rotation % 360) + 360) % 360,
            })
            .ToList(),
    };

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private async Task SaveAsync()
    {
        if (SelectedSection is not { } section || !IsDirty)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _layoutApi.SaveAsync(section.Id, BuildRequest());
            if (result.Success && result.Data is not null)
            {
                _notifications.Success("Floor layout saved.");
                ApplyLayout(result.Data);
                return;
            }

            _notifications.Error(ApiFailures.Describe(result));
            if (!result.IsConnectionFailure)
            {
                // The server refused (conflict, table removed, limits): show what is stored now.
                await LoadLayoutAsync();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- helpers ----

    private static (int X, int Y) GridSlot(int index) =>
        (GridGap + (index % GridColumns) * (DefaultWidth + GridGap), GridGap + (index / GridColumns) * (DefaultHeight + GridGap));

    private (int X, int Y) NextFreeSlot()
    {
        for (var index = 0; ; index++)
        {
            var (x, y) = GridSlot(index);
            var free = !Placed.Any(p => x < p.X + p.Width && p.X < x + DefaultWidth && y < p.Y + p.Height && p.Y < y + DefaultHeight);
            if (free || index > FloorLayoutLimits.MaxItems)
            {
                return (x, y);
            }
        }
    }

    private Snap[] Capture() =>
        Items.Select(i => new Snap(i.TableId, i.IsPlaced, i.X, i.Y, i.Width, i.Height, i.Shape, i.Rotation)).ToArray();

    private void Restore(Snap[] snapshot)
    {
        foreach (var snap in snapshot)
        {
            if (Items.FirstOrDefault(i => i.TableId == snap.TableId) is { } item)
            {
                item.X = snap.X;
                item.Y = snap.Y;
                item.Width = snap.Width;
                item.Height = snap.Height;
                item.Shape = snap.Shape;
                item.Rotation = snap.Rotation;
                item.IsPlaced = snap.Placed;
            }
        }

        Rebuild();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private void PushUndo(Snap[] before)
    {
        _undo.Push(before);
        _redo.Clear();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    // A change made outside a gesture records the state before it; inside a gesture EndGesture records it once.
    private void Commit(Snap[]? before)
    {
        if (before is not null && !before.SequenceEqual(Capture()))
        {
            PushUndo(before);
        }

        if (before is not null)
        {
            Rebuild();
        }
        else
        {
            RefreshDirty();
        }
    }

    private void Rebuild()
    {
        var selectedId = SelectedItem?.TableId;
        Placed.Clear();
        Unplaced.Clear();
        foreach (var item in Items)
        {
            (item.IsPlaced ? Placed : Unplaced).Add(item);
        }

        if (selectedId is { } id && Items.FirstOrDefault(i => i.TableId == id) is { IsPlaced: true } keep)
        {
            SelectedItem = keep;
        }
        else
        {
            SelectedItem = null;
        }

        OnPropertyChanged(nameof(HasUnplaced));
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
        RefreshDirty();
    }

    private void RefreshDirty()
    {
        IsDirty = !_baseline.SequenceEqual(Capture());
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
        RotateCommand.NotifyCanExecuteChanged();
        ToggleShapeCommand.NotifyCanExecuteChanged();
    }

    private void OnLayoutChanged(FloorLayoutChangedEvent change)
    {
        if (change.SectionId != SelectedSection?.Id)
        {
            return;
        }

        if (IsDirty)
        {
            _notifications.Warning("This section's layout was changed on another terminal. Saving will replace it.");
            return;
        }

        _ = LoadLayoutAsync();
    }
}
