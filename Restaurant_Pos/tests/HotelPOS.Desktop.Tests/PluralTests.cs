using HotelPOS.Contracts.Cash;
using HotelPOS.Desktop.Modules.Cash;
using HotelPOS.Desktop.Modules.Common;
using HotelPOS.Desktop.Modules.Reports;

namespace HotelPOS.Desktop.Tests;

public sealed class PluralTests
{
    [Theory]
    [InlineData(0, "0 rows")]
    [InlineData(1, "1 row")]
    [InlineData(2, "2 rows")]
    public void Count_UsesTheSingularForOne(int count, string expected) => Plural.Count(count, "row").Should().Be(expected);

    [Theory]
    [InlineData(1, "unpaid bills", "1 unpaid bill")]
    [InlineData(1, "open cash shifts", "1 open cash shift")]
    [InlineData(3, "unpaid bills", "3 unpaid bills")]
    public void CountPhrase_SingularisesAServerPhraseForOne(int count, string phrase, string expected) =>
        Plural.CountPhrase(count, phrase).Should().Be(expected);

    [Fact]
    public void DayEndItems_ReadNaturallyForOne()
    {
        var item = new DayEndCheckItemViewModel(new DayEndCheckItemDto { Code = DayEndCheckCodes.UnpaidBills, Title = "Unpaid bills", Count = 1, Details = new[] { "Bill 3" } });

        item.Title.Should().Be("1 unpaid bill");
    }

    [Theory]
    [InlineData(20.00, "Late percent", "20%")]
    [InlineData(6.90, "Average accept minutes", "6.9")]
    [InlineData(6.90, "Net sales", "6.90")]
    public void ReportCells_FormatPercentagesAndMinutesWithOneDecimal(double value, string header, string expected) =>
        ReportsViewModel.Display((decimal)value, header).Should().Be(expected);
}
