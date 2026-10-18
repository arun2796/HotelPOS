using FluentAssertions;
using HotelPOS.Contracts.Billing;
using HotelPOS.Domain.Billing;
using HotelPOS.Domain.Common;
using Xunit;

namespace HotelPOS.Domain.Tests.Billing;

public class PaymentMethodTests
{
    [Fact]
    public void WalletMethod_CanBeInstantiated_WithReferenceRequirement()
    {
        var wallet = new PaymentMethod("Wallet", PaymentMethodCodes.Wallet, requiresReference: true, sortOrder: 4);

        wallet.Name.Should().Be("Wallet");
        wallet.Code.Should().Be("WALLET");
        wallet.RequiresReference.Should().BeTrue();
        wallet.SortOrder.Should().Be(4);
        wallet.IsActive.Should().BeTrue();
        wallet.IsCash.Should().BeFalse();
    }

    [Fact]
    public void CashMethod_HasIsCashTrue_AndCannotBeRenamedFromCash()
    {
        var cash = new PaymentMethod("Cash", PaymentMethodCodes.Cash, requiresReference: false, sortOrder: 1);
        cash.IsCash.Should().BeTrue();

        // Simulate entity having Id != 0
        typeof(PaymentMethod).GetProperty("Id")!.SetValue(cash, 1);

        var act = () => cash.Update("Cash Renamed", "NOT_CASH", false, 1);
        act.Should().Throw<DomainException>().WithMessage("*cannot be renamed*");
    }

    [Theory]
    [InlineData("", "WALLET")]
    [InlineData("Valid", "")]
    public void PaymentMethod_WithInvalidNameOrCode_ThrowsDomainException(string name, string code)
    {
        var act = () => new PaymentMethod(name, code, true, 1);
        act.Should().Throw<DomainException>();
    }
}
