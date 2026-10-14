using HotelPOS.Contracts.Print;

namespace HotelPOS.Domain.Printing;

public sealed record LayoutProblem(string Field, string Message);

// What a template may change. It changes presentation, never the legally required content of a GST invoice: the
// restaurant GSTIN, the invoice number and date and the tax breakup are always printed (the layout has no switch
// for the number, date or breakup, and ShowGstin cannot be turned off on an invoice), and the item table always
// shows the item, the quantity and the amount. Problems are returned, not thrown: a rejected layout is user input.
public static class TemplateLayoutRules
{
    public static readonly IReadOnlyList<TemplateColumn> RequiredInvoiceColumns =
        new[] { TemplateColumn.Item, TemplateColumn.Quantity, TemplateColumn.Amount };

    public static IReadOnlyList<LayoutProblem> Validate(PrintDocumentType documentType, TemplateLayout? layout)
    {
        var problems = new List<LayoutProblem>();
        if (layout is null)
        {
            problems.Add(new LayoutProblem("layout", "The layout is missing."));
            return problems;
        }

        if (!Enum.IsDefined(documentType))
        {
            problems.Add(new LayoutProblem("documentType", $"Unknown document type {(int)documentType}."));
        }

        if (!Enum.IsDefined(layout.Paper))
        {
            problems.Add(new LayoutProblem("layout.paper", $"Unknown paper size {(int)layout.Paper}."));
        }

        if (!Enum.IsDefined(layout.GstBreakup))
        {
            problems.Add(new LayoutProblem("layout.gstBreakup", $"Unknown GST breakup style {(int)layout.GstBreakup}."));
        }

        if (!Enum.IsDefined(layout.FontSize))
        {
            problems.Add(new LayoutProblem("layout.fontSize", $"Unknown font size {(int)layout.FontSize}."));
        }

        Lines(problems, "layout.headerLines", "header", layout.HeaderLines);
        Lines(problems, "layout.footerLines", "footer", layout.FooterLines);
        Lines(problems, "layout.terms", "terms", layout.Terms);

        var columns = layout.Columns ?? Array.Empty<TemplateColumn>();
        foreach (var unknown in columns.Where(c => !Enum.IsDefined(c)).Distinct())
        {
            problems.Add(new LayoutProblem("layout.columns", $"Unknown column {(int)unknown}."));
        }

        foreach (var duplicate in columns.GroupBy(c => c).Where(g => g.Count() > 1).Select(g => g.Key))
        {
            problems.Add(new LayoutProblem("layout.columns", $"The {Describe(duplicate)} column is listed twice."));
        }

        if (documentType == PrintDocumentType.Invoice)
        {
            foreach (var missing in RequiredInvoiceColumns.Where(c => !columns.Contains(c)))
            {
                problems.Add(new LayoutProblem("layout.columns", $"A GST invoice must show the {Describe(missing)} column."));
            }

            if (!layout.ShowGstin)
            {
                problems.Add(new LayoutProblem("layout.showGstin", "A GST invoice must show the restaurant GSTIN."));
            }
        }

        return problems;
    }

    public static string Describe(TemplateColumn column) => column switch
    {
        TemplateColumn.Item => "item",
        TemplateColumn.HsnSac => "HSN/SAC",
        TemplateColumn.Quantity => "quantity",
        TemplateColumn.Rate => "rate",
        TemplateColumn.Discount => "discount",
        TemplateColumn.TaxPercent => "tax %",
        TemplateColumn.Amount => "amount",
        _ => ((int)column).ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    private static void Lines(List<LayoutProblem> problems, string field, string label, IReadOnlyList<string>? lines)
    {
        if (lines is null)
        {
            return;
        }

        if (lines.Count > TemplateLimits.MaxLines)
        {
            problems.Add(new LayoutProblem(field, $"At most {TemplateLimits.MaxLines} {label} lines."));
        }

        if (lines.Any(l => l is null))
        {
            problems.Add(new LayoutProblem(field, $"A {label} line is empty."));
        }
        else if (lines.Any(l => l.Length > TemplateLimits.LineMaxLength))
        {
            problems.Add(new LayoutProblem(field, $"A {label} line is longer than {TemplateLimits.LineMaxLength} characters."));
        }
    }
}
