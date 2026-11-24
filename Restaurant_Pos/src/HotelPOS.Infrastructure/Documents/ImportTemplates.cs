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
        var headers = new[] { "Category", "Item name", "Price", "Tax", "Preparation station", "Item code", "Description", "HSN/SAC", "Modifiers" };
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = i < 3 ? XLColor.FromHtml("#DCFCE7") : XLColor.FromHtml("#F1F5F9");
        }

        var samples = new[]
        {
            new[] { "Starters", "Chicken 65", "220", "5", "Main Kitchen", "ST01", "Crisp fried chicken with curry leaves", "996331", "Spice: Mild, Hot" },
            new[] { "Biryani", "Chicken Biryani", "260", "5", "Main Kitchen", "BR01", "Seeraga samba rice, served with raita", "996331", "Size: Half, Full +90; Extras: Egg +15, Raita +20" },
            new[] { "Breads", "Butter Naan", "45", "5", "Live Kitchen", "BD01", "", "996331", "" },
            new[] { "Beverages", "Fresh Lime Soda", "60", "5", "", "BV01", "Sweet or salt", "996331", "Taste: Sweet, Salt" },
        };
        for (var r = 0; r < samples.Length; r++)
        {
            for (var c = 0; c < samples[r].Length; c++)
            {
                var text = samples[r][c];
                if (c == 2 && decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var price))
                {
                    sheet.Cell(r + 2, c + 1).Value = price;
                }
                else if (text.Length > 0)
                {
                    sheet.Cell(r + 2, c + 1).Value = text;
                }
            }
        }

        sheet.Column(3).Style.NumberFormat.Format = "0.00";
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents(1, 5);

        var help = workbook.AddWorksheet("How to fill");
        var lines = new[]
        {
            ("Menu import", true),
            ("Fill the Menu sheet, one dish per row, delete the sample rows, save, then open Admin > Import in HotelPOS and choose this file.", false),
            ("", false),
            ("Needed (green headings)", true),
            ("Category - the menu group (Starters, Biryani...). A new name creates the category.", false),
            ("Item name - what the customer sees. An item with the same category and name is updated, not duplicated.", false),
            ("Price - selling price per portion, numbers only (220 or 220.50).", false),
            ("", false),
            ("Optional", true),
            ("Tax - a GST rate such as 5 or 5% (or the name of a tax you already set up). Blank uses the default.", false),
            ("Preparation station - the kitchen that cooks it (Main Kitchen, Live Kitchen, Bar...). Blank uses the first active station.", false),
            ("Item code - a short code the waiter can type to find the dish.", false),
            ("Description - one line shown on the menu.", false),
            ("HSN/SAC - the tax classification code for the invoice.", false),
            ("Modifiers - choices for the dish. Format: Group: Option, Option +extra price; Next group: Option. Example: Size: Half, Full +90; Extras: Egg +15", false),
            ("", false),
            ("Packing charge: set the restaurant-wide parcel charge in Admin > Settings, or per item under Menu Items.", false),
            ("Stock counts (portions left) are set per item under Menu Items, not in this file.", false),
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
