using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HotelPOS.Desktop.Modules.Admin;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    // Scrolls the settings list to the heading with the chosen section's name (the headings carry it as Tag).
    private void OnSectionChosen(object sender, SelectionChangedEventArgs e)
    {
        if (SectionNav.SelectedItem is string section)
        {
            ScrollTo(section, retry: true);
        }
    }

    // A deep link selects the section before the list has generated its headings: try again once the layout has run.
    private void ScrollTo(string section, bool retry)
    {
        var heading = FindHeading(SettingsScroll, section);
        if (heading is null)
        {
            if (retry)
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => ScrollTo(section, retry: false)));
            }

            return;
        }

        if (!heading.IsDescendantOf(SettingsScroll))
        {
            return;
        }

        var offset = heading.TransformToAncestor(SettingsScroll).Transform(new Point(0, 0)).Y + SettingsScroll.VerticalOffset;
        SettingsScroll.ScrollToVerticalOffset(offset);
    }

    private static Border? FindHeading(DependencyObject parent, string section)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Border { Tag: string tag } border && tag == section)
            {
                return border;
            }

            if (FindHeading(child, section) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
