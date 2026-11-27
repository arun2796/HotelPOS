using ClosedXML.Excel;

namespace HotelPOS.Infrastructure.Documents;

// The blank Excel files an owner fills in and brings back through Admin > Import. The headers are exactly the column
// names the importer recognises, so the mapping step needs no changes.
public static class ImportTemplates
{
    public static byte[] Menu()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Menu");
        var headers = new[]
        {
            "Category", "Item name", "Portion", "Portion qty", "Portion unit", "Price", "Default portion", "Portion active",
            "Tax", "Preparation station", "Item code", "Packing charge", "Description", "HSN/SAC",
            "Sort order", "Available", "Stock tracking", "Stock count", "Low stock at", "Modifiers",
        };
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            var isRequired = i is 0 or 1 or 5;
            cell.Style.Fill.BackgroundColor = isRequired ? XLColor.FromHtml("#DCFCE7") : XLColor.FromHtml("#F1F5F9");
        }

        var samples = new[]
        {
            new[] { "Starters", "Chicken 65", "Regular", "1", "PLATE", "220", "Yes", "Yes", "5", "Main Kitchen", "ST01", "10", "Crisp fried chicken with curry leaves", "996331", "10", "Yes", "Exact", "30", "5", "Spice: Mild, Hot" },
            new[] { "Biryani", "Chicken Biryani", "Half", "500", "GM", "200", "Yes", "Yes", "5", "Main Kitchen", "BR01", "20", "Seeraga samba rice, served with raita", "996331", "20", "Yes", "Exact", "40", "10", "Extras: Egg +15, Raita +20" },
            new[] { "Biryani", "Chicken Biryani", "Full", "1", "KG", "360", "No", "Yes", "", "", "", "", "", "", "", "", "", "", "", "" },
            new[] { "Breads", "Butter Naan", "Regular", "1", "PCS", "45", "Yes", "Yes", "5", "Live Kitchen", "BD01", "0", "", "996331", "30", "Yes", "None", "", "", "" },
            new[] { "Beverages", "Fresh Lime Soda", "Regular", "300", "ML", "60", "Yes", "Yes", "5", "", "BV01", "5", "Sweet or salt", "996331", "40", "Yes", "Approx", "50", "10", "Taste: Sweet, Salt" },
        };
        for (var r = 0; r < samples.Length; r++)
        {
            for (var c = 0; c < samples[r].Length; c++)
            {
                var text = samples[r][c];
                if (c is 3 or 5 or 11 or 14 or 17 or 18 && decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var num))
                {
                    sheet.Cell(r + 2, c + 1).Value = num;
                }
                else if (text.Length > 0)
                {
                    sheet.Cell(r + 2, c + 1).Value = text;
                }
            }
        }

        sheet.Column(6).Style.NumberFormat.Format = "0.00";
        sheet.Column(12).Style.NumberFormat.Format = "0.00";
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents(1, 6);

        var help = workbook.AddWorksheet("How to fill");
        var lines = new[]
        {
            ("Menu import", true),
            ("Fill the Menu sheet, delete the sample rows, save, then open Admin > Import in HotelPOS and choose this file.", false),
            ("", false),
            ("Needed (green headings)", true),
            ("Category - the menu group (Starters, Biryani...). A new name creates the category.", false),
            ("Item name - what the customer sees. An item with the same category and name is updated, not duplicated.", false),
            ("Price - selling price of this portion, numbers only (200 or 200.50).", false),
            ("", false),
            ("Portions (one row per portion)", true),
            ("Portion - size name such as Regular, Normal, Quarter, Half, Full or 1 KG. To give one dish multiple portions (like Chicken Biryani Half and Full), add one row per portion with the same Category and Item name.", false),
            ("Portion qty - optional label number (for example 500 or 1).", false),
            ("Portion unit - unit for Portion qty: GM, KG, ML, L, PCS or PLATE.", false),
            ("Default portion - Yes or No. Marks the default portion (if blank, the first active row becomes the default).", false),
            ("Portion active - Yes or No (default Yes). Set No to keep a portion on history without selling it.", false),
            ("", false),
            ("Optional dish details (fill on the first row of a multi-portion item)", true),
            ("Tax - a GST rate such as 5 or 5% (or the name of a tax you already set up). Blank uses the default.", false),
            ("Preparation station - the kitchen that cooks it (Main Kitchen, Live Kitchen, Bar...). Blank uses the first active station.", false),
            ("Item code - a short code the waiter can type to find the dish.", false),
            ("Packing charge - parcel charge added per unit (0 uses the default in Settings).", false),
            ("Description - one line shown on the menu.", false),
            ("HSN/SAC - the 4 to 8 digit tax classification code for the invoice.", false),
            ("Sort order - display order inside its category (0 to 9999).", false),
            ("Available - Yes or No (default Yes). Set No if currently sold out.", false),
            ("Stock tracking - None, Exact (blocks orders at 0) or Approx (estimated count).", false),
            ("Stock count - opening / available count (0 to 100000) when Stock tracking is Exact or Approx.", false),
            ("Low stock at - low-stock warning threshold (0 to 100000).", false),
            ("Modifiers - extra choices for the dish. Format: Group: Option, Option +extra price; Next group: Option. Example: Spice: Mild, Hot; Extras: Egg +15, Raita +20", false),
        };
        for (var i = 0; i < lines.Length; i++)
        {
            var cell = help.Cell(i + 1, 1);
            cell.Value = lines[i].Item1;
            cell.Style.Font.Bold = lines[i].Item2;
        }

        help.Column(1).Width = 120;
        help.Column(1).Style.Alignment.WrapText = true;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static byte[] Customers()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Customers");
        var headers = new[] { "Name", "Phone", "Email", "GSTIN", "Legal name", "State code", "Address" };
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = i == 0 ? XLColor.FromHtml("#DCFCE7") : XLColor.FromHtml("#F1F5F9");
        }

        sheet.Cell(2, 1).Value = "Rahul Traders";
        sheet.Cell(2, 2).Value = "9876543210";
        sheet.Cell(2, 3).Value = "rahul@example.com";
        sheet.Cell(2, 4).Value = "33ABCDE1234F1Z5";
        sheet.Cell(2, 5).Value = "Rahul Traders Pvt Ltd";
        sheet.Cell(2, 6).Value = "33";
        sheet.Cell(2, 7).Value = "12 Anna Salai, Chennai";
        sheet.Column(2).Style.NumberFormat.Format = "@";
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents(1, 3);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
