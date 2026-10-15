using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace HotelPOS.Desktop.Controls;

// The filter strip of reports and logs: a period preset, a custom from/to range (enabled only for the custom preset),
// an optional search box, a Run button and the screen's own actions (Content) on the right.
public sealed class ReportFilterBar : ContentControl
{
    public static readonly DependencyProperty PresetsProperty =
        DependencyProperty.Register(nameof(Presets), typeof(IEnumerable), typeof(ReportFilterBar));

    public static readonly DependencyProperty SelectedPresetProperty =
        DependencyProperty.Register(nameof(SelectedPreset), typeof(object), typeof(ReportFilterBar),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty FromDateProperty =
        DependencyProperty.Register(nameof(FromDate), typeof(DateTime?), typeof(ReportFilterBar),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty ToDateProperty =
        DependencyProperty.Register(nameof(ToDate), typeof(DateTime?), typeof(ReportFilterBar),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty IsCustomRangeProperty =
        DependencyProperty.Register(nameof(IsCustomRange), typeof(bool), typeof(ReportFilterBar), new PropertyMetadata(true));

    public static readonly DependencyProperty ShowRangeProperty =
        DependencyProperty.Register(nameof(ShowRange), typeof(bool), typeof(ReportFilterBar), new PropertyMetadata(true));

    public static readonly DependencyProperty RunCommandProperty =
        DependencyProperty.Register(nameof(RunCommand), typeof(ICommand), typeof(ReportFilterBar));

    public static readonly DependencyProperty RunTextProperty =
        DependencyProperty.Register(nameof(RunText), typeof(string), typeof(ReportFilterBar), new PropertyMetadata("Run"));

    public static readonly DependencyProperty SearchTextProperty =
        DependencyProperty.Register(nameof(SearchText), typeof(string), typeof(ReportFilterBar),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, null, null, false,
                UpdateSourceTrigger.PropertyChanged));

    public static readonly DependencyProperty ShowSearchProperty =
        DependencyProperty.Register(nameof(ShowSearch), typeof(bool), typeof(ReportFilterBar), new PropertyMetadata(false));

    public static readonly DependencyProperty SearchHintProperty =
        DependencyProperty.Register(nameof(SearchHint), typeof(string), typeof(ReportFilterBar), new PropertyMetadata("Search"));

    // Extra controls between the range and the Run button (e.g. a user or action filter).
    public static readonly DependencyProperty FiltersProperty =
        DependencyProperty.Register(nameof(Filters), typeof(object), typeof(ReportFilterBar));

    public IEnumerable? Presets
    {
        get => (IEnumerable?)GetValue(PresetsProperty);
        set => SetValue(PresetsProperty, value);
    }

    public object? SelectedPreset
    {
        get => GetValue(SelectedPresetProperty);
        set => SetValue(SelectedPresetProperty, value);
    }

    public DateTime? FromDate
    {
        get => (DateTime?)GetValue(FromDateProperty);
        set => SetValue(FromDateProperty, value);
    }

    public DateTime? ToDate
    {
        get => (DateTime?)GetValue(ToDateProperty);
        set => SetValue(ToDateProperty, value);
    }

    public bool IsCustomRange
    {
        get => (bool)GetValue(IsCustomRangeProperty);
        set => SetValue(IsCustomRangeProperty, value);
    }

    public bool ShowRange
    {
        get => (bool)GetValue(ShowRangeProperty);
        set => SetValue(ShowRangeProperty, value);
    }

    public ICommand? RunCommand
    {
        get => (ICommand?)GetValue(RunCommandProperty);
        set => SetValue(RunCommandProperty, value);
    }

    public string RunText
    {
        get => (string)GetValue(RunTextProperty);
        set => SetValue(RunTextProperty, value);
    }

    public string SearchText
    {
        get => (string)GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    public bool ShowSearch
    {
        get => (bool)GetValue(ShowSearchProperty);
        set => SetValue(ShowSearchProperty, value);
    }

    public string SearchHint
    {
        get => (string)GetValue(SearchHintProperty);
        set => SetValue(SearchHintProperty, value);
    }

    public object? Filters
    {
        get => GetValue(FiltersProperty);
        set => SetValue(FiltersProperty, value);
    }
}
