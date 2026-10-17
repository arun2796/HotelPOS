using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace HotelPOS.Desktop.Controls;

// Prev / Next with the range in between ("1–20 of 186"). PageIndex is zero based. The control only moves PageIndex
// (two-way bindable); the screen reloads on the PageChanged event or by binding PageChangedCommand, which receives the new index.
public sealed class Pager : Control
{
    public static readonly DependencyProperty PageIndexProperty =
        DependencyProperty.Register(nameof(PageIndex), typeof(int), typeof(Pager),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPagingChanged, CoercePageIndex));

    public static readonly DependencyProperty PageSizeProperty =
        DependencyProperty.Register(nameof(PageSize), typeof(int), typeof(Pager), new PropertyMetadata(20, OnPagingChanged));

    public static readonly DependencyProperty TotalCountProperty =
        DependencyProperty.Register(nameof(TotalCount), typeof(int), typeof(Pager), new PropertyMetadata(0, OnPagingChanged));

    public static readonly DependencyProperty PageChangedCommandProperty =
        DependencyProperty.Register(nameof(PageChangedCommand), typeof(ICommand), typeof(Pager), new PropertyMetadata(null));

    private static readonly DependencyPropertyKey RangeTextPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(RangeText), typeof(string), typeof(Pager), new PropertyMetadata("0 of 0"));

    public static readonly DependencyProperty RangeTextProperty = RangeTextPropertyKey.DependencyProperty;

    private readonly RelayCommand _previous;
    private readonly RelayCommand _next;

    static Pager()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Pager), new FrameworkPropertyMetadata(typeof(Pager)));
        FocusableProperty.OverrideMetadata(typeof(Pager), new FrameworkPropertyMetadata(false));
    }

    public Pager()
    {
        _previous = new RelayCommand(() => PageIndex--, () => PageIndex > 0);
        _next = new RelayCommand(() => PageIndex++, () => PageIndex < LastPageIndex);
        Update();
    }

    public event EventHandler<int>? PageChanged;

    public int PageIndex
    {
        get => (int)GetValue(PageIndexProperty);
        set => SetValue(PageIndexProperty, value);
    }

    public int PageSize
    {
        get => (int)GetValue(PageSizeProperty);
        set => SetValue(PageSizeProperty, value);
    }

    public int TotalCount
    {
        get => (int)GetValue(TotalCountProperty);
        set => SetValue(TotalCountProperty, value);
    }

    public ICommand? PageChangedCommand
    {
        get => (ICommand?)GetValue(PageChangedCommandProperty);
        set => SetValue(PageChangedCommandProperty, value);
    }

    public string RangeText => (string)GetValue(RangeTextProperty);

    public ICommand PreviousCommand => _previous;

    public ICommand NextCommand => _next;

    private int LastPageIndex => PageSize <= 0 ? 0 : Math.Max(0, (TotalCount - 1) / PageSize);

    /// <summary>"1–20 of 186", or "0 of 0" when there is nothing.</summary>
    public static string Describe(int pageIndex, int pageSize, int totalCount)
    {
        if (totalCount <= 0 || pageSize <= 0)
        {
            return "0 of 0";
        }

        var from = pageIndex * pageSize + 1;
        var to = Math.Min(totalCount, (pageIndex + 1) * pageSize);
        return string.Create(CultureInfo.CurrentCulture, $"{from}–{to} of {totalCount}");
    }

    private static object CoercePageIndex(DependencyObject d, object value)
    {
        var pager = (Pager)d;
        return Math.Clamp((int)value, 0, pager.LastPageIndex);
    }

    private static void OnPagingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var pager = (Pager)d;
        pager.CoerceValue(PageIndexProperty);
        pager.Update();
        if (e.Property == PageIndexProperty)
        {
            pager.PageChanged?.Invoke(pager, (int)e.NewValue);
            if (pager.PageChangedCommand is { } command && command.CanExecute(e.NewValue))
            {
                command.Execute(e.NewValue);
            }
        }
    }

    private void Update()
    {
        SetValue(RangeTextPropertyKey, Describe(PageIndex, PageSize, TotalCount));
        _previous.NotifyCanExecuteChanged();
        _next.NotifyCanExecuteChanged();
    }
}
