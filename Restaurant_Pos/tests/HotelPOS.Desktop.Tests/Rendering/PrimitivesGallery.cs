using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using HotelPOS.Contracts.Enums;
using HotelPOS.Contracts.Presentation;
using HotelPOS.Desktop.Controls;
using Wpf = System.Windows.Controls;

namespace HotelPOS.Desktop.Tests.Rendering;

/// <summary>
/// One screen with every Phase 21 desktop primitive in it, rendered by <see cref="ScreenRenderingTests"/> in both themes: it
/// instantiates and measures every template, so a bad TemplateBinding or a missing resource fails the rendering test.
/// </summary>
internal static class PrimitivesGallery
{
    public static FrameworkElement Build()
    {
        var root = new StackPanel { Margin = new Thickness(24) };

        root.Children.Add(Row(
            Badge(StatusPresentation.For(OrderStatus.Preparing)),
            Badge(StatusPresentation.For(OrderStatus.Ready)),
            Badge(StatusPresentation.For(OrderStatus.Paid)),
            Badge(StatusPresentation.For(OrderStatus.Cancelled)),
            Badge(StatusPresentation.For(OrderStatus.Draft)),
            Badge(StatusPresentation.For(TableStatus.Available)),
            Badge(StatusPresentation.For(TableStatus.Billing)),
            Badge(StatusPresentation.Offline),
            new Icon { Kind = "home" }, new Icon { Kind = "table" }, new Icon { Kind = "bell" }, new Icon { Kind = "search" },
            new Icon { Kind = "sun" }, new Icon { Kind = "moon" }, new Icon { Kind = "logout" }, new Icon { Kind = "wifi-off" }));

        root.Children.Add(Row(
            Button("Primary", "Button.Primary"), Button("Secondary", "Button.Secondary"), Button("Outline", "Button.PrimaryOutline"),
            Button("Mark ready", "Button.Warning"), Button("Cancel", "Button.Danger"), Button("Pay now", "Button.Large")));

        var tiles = new UniformGrid { Columns = 3 };
        tiles.Children.Add(new KpiTile { Title = "Today's sales", Value = "₹ 48,250", Icon = "receipt", Tone = StatusTone.Success, Delta = 12.5, Detail = "vs yesterday", Spark = new double[] { 3, 5, 4, 8, 7, 9, 12 } });
        tiles.Children.Add(new KpiTile { Title = "Cancellations", Value = "4", Icon = "ban", Tone = StatusTone.Danger, Delta = 33.0, LowerIsBetter = true, Detail = "vs yesterday" });
        tiles.Children.Add(new KpiTile { Title = "Open orders", Value = "17", Glyph = "", Tone = StatusTone.Info, Detail = "6 tables ordering" });
        root.Children.Add(tiles);

        var table = new Wpf.DataGrid { Height = 150, Margin = new Thickness(0, 8, 0, 8), ItemsSource = new[]
        {
            new GalleryRow("Order 101", StatusPresentation.For(OrderStatus.Preparing)),
            new GalleryRow("Order 102", StatusPresentation.For(OrderStatus.Ready)),
            new GalleryRow("Order 103", StatusPresentation.For(OrderStatus.Cancelled)),
        } };
        table.Columns.Add(new DataGridTextColumn { Header = "Order", Binding = new System.Windows.Data.Binding(nameof(GalleryRow.Name)), Width = 200 });
        table.Columns.Add(new StatusBadgeColumn { Header = "Status", Binding = new System.Windows.Data.Binding(nameof(GalleryRow.Display)), Width = 200 });
        root.Children.Add(table);

        root.Children.Add(Row(
            new Pager { PageSize = 20, TotalCount = 186, PageIndex = 1 },
            new SearchBox { Width = 260, Placeholder = "Search or jump to…", ShortcutHint = "Ctrl K" },
            Segmented("Today", "7 Days", "30 Days"),
            Tabs("Details", "Items", "Timeline")));

        root.Children.Add(new OrderLine { Quantity = 2, ItemName = "Masala dosa", Modifiers = "Extra chutney, no onion", Note = "Customer is allergic to peanuts", PriceText = "₹ 240.00", ImageSize = 52 });

        root.Children.Add(new ErrorState { Message = "The bills could not be loaded. Check the connection.", RetryCommand = new RelayStub() });
        root.Children.Add(new EmptyState { Icon = "receipt", Title = "No bills waiting", Message = "New requests appear here instantly." });

        var payment = new Border { Style = (Style)Application.Current.FindResource("PaymentPanel"), Width = 340, HorizontalAlignment = HorizontalAlignment.Left };
        var pay = new StackPanel();
        pay.Children.Add(new TextBlock { Text = "₹ 1,260.00", Style = (Style)Application.Current.FindResource("PaymentPanel.Due") });
        var methods = new WrapPanel();
        foreach (var name in new[] { "Cash", "Card", "UPI", "Split" })
        {
            methods.Children.Add(new ToggleButton { Content = name, IsChecked = name == "Cash", Width = 150, Style = (Style)Application.Current.FindResource("PaymentPanel.Method") });
        }

        pay.Children.Add(methods);
        pay.Children.Add(new Border
        {
            Style = (Style)Application.Current.FindResource("PaymentPanel.Change"),
            Child = new TextBlock { Text = "Change ₹ 40.00", Style = (Style)Application.Current.FindResource("PaymentPanel.ChangeAmount") },
        });
        payment.Child = pay;
        root.Children.Add(payment);

        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
    }

    private static StatusBadge Badge(StatusDisplay display) => new() { Display = display, Margin = new Thickness(0, 0, 8, 0) };

    private static Wpf.Button Button(string text, string style) =>
        new() { Content = text, Style = (Style)Application.Current.FindResource(style), Margin = new Thickness(0, 0, 8, 0) };

    private static ListBox Segmented(params string[] items) => List("Segmented", items);

    private static ListBox Tabs(params string[] items) => List("Tabs", items);

    private static ListBox List(string style, string[] items)
    {
        var list = new ListBox { Style = (Style)Application.Current.FindResource(style), ItemsSource = items, SelectedIndex = 0, Margin = new Thickness(12, 0, 0, 0) };
        return list;
    }

    private static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        foreach (var child in children)
        {
            if (child is FrameworkElement element && element is not Wpf.Button && element.Margin == default)
            {
                element.Margin = new Thickness(0, 0, 8, 0);
            }

            row.Children.Add(child);
        }

        return row;
    }

    internal sealed record GalleryRow(string Name, StatusDisplay Display);

    private sealed class RelayStub : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
        }
    }
}
