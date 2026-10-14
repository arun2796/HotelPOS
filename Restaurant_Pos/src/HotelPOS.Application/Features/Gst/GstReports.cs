using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using HotelPOS.Contracts.Gst;
using HotelPOS.Domain.Gst;

namespace HotelPOS.Application.Features.Gst;

// Turns the period data into the screens' rows (GSTR-1 sections, GSTR-3B, HSN summary, validation) and the portal JSON.
internal static class GstReports
{
    private static decimal R(decimal value) => GstDataLoader.Money(value);

    public static Gstr1Dto Gstr1(GstPeriodData data)
    {
        var b2b = data.Invoices.Where(i => i.Type == GstSupplyType.B2B).Select(InvoiceRow).ToList();
        var b2cl = data.Invoices.Where(i => i.Type == GstSupplyType.B2CL).Select(InvoiceRow).ToList();
        var notes = data.Notes.Where(n => n.InvoiceType != GstSupplyType.B2CS).Select(n => NoteRow(n, n.Ctin is null ? "CDNUR" : "CDNR")).ToList();
        var issues = Validation(data);
        var tax = data.Invoices.Aggregate(default(GstSplit), (sum, i) => sum + i.Tax);
        return new Gstr1Dto
        {
            Period = data.Key,
            FilingPeriod = data.FilingPeriod,
            Gstin = data.Settings.Gstin,
            B2clThreshold = data.Settings.B2clThreshold,
            Totals = new GstTotalsDto
            {
                Invoices = data.Invoices.Count,
                InvoiceValue = data.Invoices.Sum(i => i.Value),
                TaxableValue = data.Invoices.Sum(i => i.Taxable),
                Igst = tax.Igst,
                Cgst = tax.Cgst,
                Sgst = tax.Sgst,
                CreditNoteTaxable = data.Notes.Sum(n => n.Taxable),
                CreditNoteTax = data.Notes.Sum(n => n.Tax.Total),
            },
            B2b = b2b,
            B2cl = b2cl,
            B2cs = B2cs(data),
            CreditNotes = notes,
            Hsn = Hsn(data),
            Documents = data.Documents.Select(d => new Gstr1DocumentRowDto
            {
                Document = d.Document,
                From = d.From,
                To = d.To,
                TotalIssued = d.Total,
                Cancelled = d.Cancelled,
                NetIssued = d.Total - d.Cancelled,
            }).ToList(),
            IssueCount = issues.Count,
            ErrorCount = issues.Count(i => i.Severity == GstIssueSeverity.Error),
        };
    }

    public static IReadOnlyList<Gstr1B2csRowDto> B2cs(GstPeriodData data)
    {
        // Unregistered B2CS invoices by supply type, place of supply and rate; refunds of those invoices reduce the rows.
        var lines = data.Invoices.Where(i => i.Type == GstSupplyType.B2CS)
            .SelectMany(i => i.Lines.Select(l => (i.InterState, i.Pos, Line: l)))
            .Concat(data.Notes.Where(n => n.InvoiceType == GstSupplyType.B2CS)
                .SelectMany(n => n.Lines.Select(l => (n.InterState, n.Pos, Line: new GstRateLine(l.Rate, -l.Taxable, new GstSplit(-l.Tax.Igst, -l.Tax.Cgst, -l.Tax.Sgst))))));
        return lines
            .GroupBy(l => (l.InterState, l.Pos, l.Line.Rate))
            .OrderBy(g => g.Key.InterState).ThenBy(g => g.Key.Pos, StringComparer.Ordinal).ThenBy(g => g.Key.Rate)
            .Select(g => new Gstr1B2csRowDto
            {
                SupplyType = g.Key.InterState ? "INTER" : "INTRA",
                PlaceOfSupply = g.Key.Pos,
                Rate = g.Key.Rate,
                TaxableValue = g.Sum(l => l.Line.Taxable),
                Igst = g.Sum(l => l.Line.Tax.Igst),
                Cgst = g.Sum(l => l.Line.Tax.Cgst),
                Sgst = g.Sum(l => l.Line.Tax.Sgst),
            }).ToList();
    }

    public static IReadOnlyList<HsnSummaryRowDto> Hsn(GstPeriodData data) => data.Hsn
        .GroupBy(h => (h.Hsn, h.Rate))
        .OrderBy(g => g.Key.Hsn, StringComparer.Ordinal).ThenBy(g => g.Key.Rate)
        .Select(g =>
        {
            var tax = g.Aggregate(default(GstSplit), (sum, h) => sum + h.Tax);
            var taxable = g.Sum(h => h.Taxable);
            return new HsnSummaryRowDto
            {
                HsnSac = g.Key.Hsn,
                Description = g.Key.Hsn == data.Settings.DefaultSac ? "Restaurant service" : string.Empty,
                Rate = g.Key.Rate,
                Quantity = g.Sum(h => h.Quantity),
                TotalValue = taxable + tax.Total,
                TaxableValue = taxable,
                Igst = tax.Igst,
                Cgst = tax.Cgst,
                Sgst = tax.Sgst,
            };
        }).ToList();

    public static Gstr3bDto Gstr3b(GstPeriodData data)
    {
        // 3.1(a) taxable supplies and 3.1(c) nil-rated ones, net of credit notes (credit notes carry the same rate groups).
        var lines = data.Invoices.SelectMany(i => i.Lines.Select(l => (i.InterState, i.Pos, i.Type, Line: l)))
            .Concat(data.Notes.SelectMany(n => n.Lines.Select(l => (n.InterState, n.Pos, Type: n.InvoiceType,
                Line: new GstRateLine(l.Rate, -l.Taxable, new GstSplit(-l.Tax.Igst, -l.Tax.Cgst, -l.Tax.Sgst))))))
            .ToList();
        Gstr3bTableRowDto Row(string table, string description, IEnumerable<(bool InterState, string Pos, GstSupplyType Type, GstRateLine Line)> source)
        {
            var list = source.ToList();
            return new Gstr3bTableRowDto
            {
                Table = table,
                Description = description,
                TaxableValue = list.Sum(l => l.Line.Taxable),
                Igst = list.Sum(l => l.Line.Tax.Igst),
                Cgst = list.Sum(l => l.Line.Tax.Cgst),
                Sgst = list.Sum(l => l.Line.Tax.Sgst),
            };
        }

        var rows = new List<Gstr3bTableRowDto>
        {
            Row("3.1(a)", "Outward taxable supplies (other than zero rated, nil rated and exempted)", lines.Where(l => l.Line.Rate > 0)),
            Row("3.1(c)", "Other outward supplies (nil rated, exempted)", lines.Where(l => l.Line.Rate == 0)),
        };
        foreach (var pos in lines.Where(l => l.InterState && l.Type != GstSupplyType.B2B && l.Line.Rate > 0).GroupBy(l => l.Pos).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            rows.Add(Row("3.2", $"Inter-state supplies to unregistered persons, place of supply {GstStates.Label(pos.Key)}", pos));
        }

        return new Gstr3bDto { Period = data.Key, FilingPeriod = data.FilingPeriod, Gstin = data.Settings.Gstin, Rows = rows };
    }

    public static GstValidationDto ValidationReport(GstPeriodData data)
    {
        var issues = Validation(data);
        return new GstValidationDto { Period = data.Key, CanFile = issues.All(i => i.Severity != GstIssueSeverity.Error), Issues = issues };
    }

    public static IReadOnlyList<GstValidationRowDto> Validation(GstPeriodData data) => data.Issues
        .OrderBy(i => i.Severity == GstIssueSeverity.Error ? 0 : 1).ThenBy(i => i.Code, StringComparer.Ordinal).ThenBy(i => i.Reference, StringComparer.Ordinal)
        .ToList();

    private static Gstr1InvoiceRowDto InvoiceRow(GstInvoice i) => new()
    {
        InvoiceNumber = i.Number,
        InvoiceDate = i.Date,
        CustomerGstin = i.Ctin ?? string.Empty,
        CustomerName = i.CustomerName,
        PlaceOfSupply = GstStates.Label(i.Pos),
        InvoiceValue = i.Value,
        TaxableValue = i.Taxable,
        Igst = i.Tax.Igst,
        Cgst = i.Tax.Cgst,
        Sgst = i.Tax.Sgst,
    };

    private static Gstr1NoteRowDto NoteRow(GstNote n, string section) => new()
    {
        Section = section,
        NoteNumber = n.Number,
        NoteDate = n.Date,
        InvoiceNumber = n.InvoiceNumber,
        CustomerGstin = n.Ctin ?? string.Empty,
        PlaceOfSupply = GstStates.Label(n.Pos),
        NoteValue = n.Value,
        TaxableValue = n.Taxable,
        Igst = n.Tax.Igst,
        Cgst = n.Tax.Cgst,
        Sgst = n.Tax.Sgst,
    };

    // The GST portal offline tool GSTR-1 JSON (field names as in the published schema; amounts rounded to 2 decimals,
    // dates dd-MM-yyyy). Aggregate turnover (gt, cur_gt) is not known to the POS: enter it in the tool.
    public static string Gstr1Json(GstPeriodData data)
    {
        var root = new JsonObject
        {
            ["gstin"] = data.Settings.Gstin,
            ["fp"] = data.FilingPeriod,
            ["version"] = "GST3.1.6",
            ["hash"] = "hash",
            ["gt"] = 0m,
            ["cur_gt"] = 0m,
        };

        var b2b = new JsonArray();
        foreach (var customer in data.Invoices.Where(i => i.Type == GstSupplyType.B2B).GroupBy(i => i.Ctin!).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var inv = new JsonArray();
            foreach (var i in customer)
            {
                inv.Add(new JsonObject
                {
                    ["inum"] = i.Number,
                    ["idt"] = Date(i.Date),
                    ["val"] = R(i.Value),
                    ["pos"] = i.Pos,
                    ["rchrg"] = "N",
                    ["inv_typ"] = "R",
                    ["itms"] = Items(i.Lines, i.InterState),
                });
            }

            b2b.Add(new JsonObject { ["ctin"] = customer.Key, ["inv"] = inv });
        }

        var b2cl = new JsonArray();
        foreach (var place in data.Invoices.Where(i => i.Type == GstSupplyType.B2CL).GroupBy(i => i.Pos).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var inv = new JsonArray();
            foreach (var i in place)
            {
                inv.Add(new JsonObject { ["inum"] = i.Number, ["idt"] = Date(i.Date), ["val"] = R(i.Value), ["itms"] = Items(i.Lines, i.InterState) });
            }

            b2cl.Add(new JsonObject { ["pos"] = place.Key, ["inv"] = inv });
        }

        var b2cs = new JsonArray();
        foreach (var row in B2cs(data))
        {
            var inter = row.SupplyType == "INTER";
            var node = new JsonObject
            {
                ["sply_ty"] = row.SupplyType,
                ["rt"] = row.Rate,
                ["typ"] = "OE",
                ["pos"] = row.PlaceOfSupply,
                ["txval"] = R(row.TaxableValue),
            };
            Heads(node, new GstSplit(row.Igst, row.Cgst, row.Sgst), inter);
            b2cs.Add(node);
        }

        var cdnr = new JsonArray();
        foreach (var customer in data.Notes.Where(n => n.InvoiceType != GstSupplyType.B2CS && n.Ctin is not null).GroupBy(n => n.Ctin!).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var nt = new JsonArray();
            foreach (var n in customer)
            {
                nt.Add(new JsonObject
                {
                    ["ntty"] = "C",
                    ["nt_num"] = n.Number,
                    ["nt_dt"] = Date(n.Date),
                    ["val"] = R(n.Value),
                    ["pos"] = n.Pos,
                    ["rchrg"] = "N",
                    ["inv_typ"] = "R",
                    ["itms"] = Items(n.Lines, n.InterState),
                });
            }

            cdnr.Add(new JsonObject { ["ctin"] = customer.Key, ["nt"] = nt });
        }

        var cdnur = new JsonArray();
        foreach (var n in data.Notes.Where(n => n.InvoiceType == GstSupplyType.B2CL && n.Ctin is null))
        {
            cdnur.Add(new JsonObject
            {
                ["typ"] = "B2CL",
                ["ntty"] = "C",
                ["nt_num"] = n.Number,
                ["nt_dt"] = Date(n.Date),
                ["val"] = R(n.Value),
                ["pos"] = n.Pos,
                ["itms"] = Items(n.Lines, n.InterState),
            });
        }

        var hsn = new JsonArray();
        var serial = 1;
        foreach (var row in Hsn(data))
        {
            var node = new JsonObject
            {
                ["num"] = serial++,
                ["hsn_sc"] = row.HsnSac,
                ["desc"] = row.Description,
                ["uqc"] = "NA",
                ["qty"] = 0m,
                ["rt"] = row.Rate,
                ["val"] = R(row.TotalValue),
                ["txval"] = R(row.TaxableValue),
                ["iamt"] = R(row.Igst),
                ["camt"] = R(row.Cgst),
                ["samt"] = R(row.Sgst),
                ["csamt"] = 0m,
            };
            hsn.Add(node);
        }

        var documents = new JsonArray();
        foreach (var group in data.Documents.GroupBy(d => (d.DocNum, d.Document)).OrderBy(g => g.Key.DocNum))
        {
            var docs = new JsonArray();
            var number = 1;
            foreach (var d in group)
            {
                docs.Add(new JsonObject { ["num"] = number++, ["from"] = d.From, ["to"] = d.To, ["totnum"] = d.Total, ["cancel"] = d.Cancelled, ["net_issue"] = d.Total - d.Cancelled });
            }

            documents.Add(new JsonObject { ["doc_num"] = group.Key.DocNum, ["doc_typ"] = group.Key.Document, ["docs"] = docs });
        }

        AddIfAny(root, "b2b", b2b);
        AddIfAny(root, "b2cl", b2cl);
        AddIfAny(root, "b2cs", b2cs);
        AddIfAny(root, "cdnr", cdnr);
        AddIfAny(root, "cdnur", cdnur);
        if (hsn.Count > 0)
        {
            root["hsn"] = new JsonObject { ["data"] = hsn };
        }

        if (documents.Count > 0)
        {
            root["doc_issue"] = new JsonObject { ["doc_det"] = documents };
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static void AddIfAny(JsonObject root, string name, JsonArray array)
    {
        if (array.Count > 0)
        {
            root[name] = array;
        }
    }

    // Item serial = rate x 100 + 1, the numbering the offline tool itself uses (5% is 501).
    private static JsonArray Items(IReadOnlyList<GstRateLine> lines, bool inter)
    {
        var items = new JsonArray();
        foreach (var line in lines)
        {
            var detail = new JsonObject { ["rt"] = line.Rate, ["txval"] = R(line.Taxable) };
            Heads(detail, line.Tax, inter);
            items.Add(new JsonObject { ["num"] = (int)(line.Rate * 100) + 1, ["itm_det"] = detail });
        }

        return items;
    }

    private static void Heads(JsonObject node, GstSplit tax, bool inter)
    {
        if (inter)
        {
            node["iamt"] = R(tax.Igst);
        }
        else
        {
            node["camt"] = R(tax.Cgst);
            node["samt"] = R(tax.Sgst);
        }

        node["csamt"] = 0m;
    }

    private static string Date(DateOnly date) => date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
}
