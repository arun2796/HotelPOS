using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace HotelPOS.Desktop.Controls;

// A list that re-selects the record just created or changed also has to show it: a view-model sets SelectedItem after
// a save, and WPF leaves the scroll position where it was, so the new row can sit below the fold.
public static class SelectorBehaviors
{
    public static readonly DependencyProperty ScrollSelectedIntoViewProperty = DependencyProperty.RegisterAttached(
        "ScrollSelectedIntoView", typeof(bool), typeof(SelectorBehaviors), new PropertyMetadata(false, OnChanged));

    public static bool GetScrollSelectedIntoView(DependencyObject element) => (bool)element.GetValue(ScrollSelectedIntoViewProperty);

    public static void SetScrollSelectedIntoView(DependencyObject element, bool value) => element.SetValue(ScrollSelectedIntoViewProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Selector selector)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            selector.SelectionChanged += OnSelectionChanged;
            selector.Loaded += OnLoaded;
        }
        else
        {
            selector.SelectionChanged -= OnSelectionChanged;
            selector.Loaded -= OnLoaded;
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => Reveal((Selector)sender);

    private static void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => Reveal((Selector)sender);

    // After the rows have been generated, otherwise a list that was just refilled has nothing to scroll to yet.
    private static void Reveal(Selector selector)
    {
        var item = selector.SelectedItem;
        if (item is null)
        {
            return;
        }

        // Once now (the rows may already exist) and once more after layout, for a list that was just refilled.
        Scroll(selector, item);
        selector.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (ReferenceEquals(selector.SelectedItem, item))
            {
                selector.UpdateLayout();
                Scroll(selector, item);
            }
        });
    }

    private static void Scroll(Selector selector, object item)
    {
        switch (selector)
        {
            case DataGrid grid:
                grid.ScrollIntoView(item);
                break;
            case ListBox list:
                list.ScrollIntoView(item);
                break;
        }
    }
}
