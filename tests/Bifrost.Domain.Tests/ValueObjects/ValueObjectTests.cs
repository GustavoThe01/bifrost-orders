using Bifrost.Domain.Common;
using Bifrost.Domain.ValueObjects;

namespace Bifrost.Domain.Tests.ValueObjects;

public class MoneyTests
{
    [Fact]
    public void Multiply_And_Add_AreExact()
    {
        var total = Money.Create(19.90m, "BRL") * 3 + Money.Create(0.30m, "BRL");

        Assert.Equal(Money.Create(60.00m, "BRL"), total);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.005)]
    public void Create_RejectsNegativeOrMoreThanTwoDecimals(decimal amount)
    {
        Assert.Throws<DomainException>(() => Money.Create(amount, "BRL"));
    }

    [Fact]
    public void Add_RejectsDifferentCurrencies()
    {
        Assert.Throws<DomainException>(() => Money.Create(1m, "BRL") + Money.Create(1m, "EUR"));
    }
}

public class EmailTests
{
    [Fact]
    public void Create_NormalizesToLowerCase()
    {
        Assert.Equal("gustavo@exemplo.com", Email.Create("  Gustavo@Exemplo.COM ").Value);
    }

    [Fact]
    public void Equality_IsByValue()
    {
        Assert.Equal(Email.Create("ana@mail.com"), Email.Create("ANA@mail.com"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("sem-arroba.com")]
    [InlineData("dois@@mail.com")]
    [InlineData("ana@mail")]
    [InlineData("ana lima@mail.com")]
    public void Create_RejectsInvalidEmails(string value)
    {
        Assert.Throws<DomainException>(() => Email.Create(value));
    }
}

public class AddressTests
{
    [Fact]
    public void Create_NormalizesStateAndZipCode()
    {
        var address = Address.Create(" Av. Beira Mar ", "100", "Fortaleza", "ce", "60165-121");

        Assert.Equal("CE", address.State);
        Assert.Equal("60165121", address.ZipCode);
        Assert.Equal("Av. Beira Mar", address.Street);
    }

    [Theory]
    [InlineData("XX", "60165121")]
    [InlineData("CE", "6016512")]
    [InlineData("CE", "")]
    public void Create_RejectsInvalidStateOrZipCode(string state, string zipCode)
    {
        Assert.Throws<DomainException>(() => Address.Create("Rua A", "1", "Fortaleza", state, zipCode));
    }

    [Fact]
    public void Create_RequiresStreet()
    {
        Assert.Throws<DomainException>(() => Address.Create(" ", "1", "Fortaleza", "CE", "60165121"));
    }
}
