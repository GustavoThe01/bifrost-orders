using Bifrost.Domain.Catalog;
using Bifrost.Domain.Common;
using Bifrost.Domain.ValueObjects;

namespace Bifrost.Domain.Tests.Catalog;

public class ProductTests
{
    private static Money Brl(decimal amount) => Money.Create(amount, "BRL");

    [Fact]
    public void Create_NormalizesSkuAndName()
    {
        var product = Product.Create(" can-001 ", "  Caneca  ", Brl(30m), 5);

        Assert.Equal("CAN-001", product.Sku);
        Assert.Equal("Caneca", product.Name);
    }

    [Theory]
    [InlineData("AB", "Caneca", 30, 5)]
    [InlineData("CAN-001", " ", 30, 5)]
    [InlineData("CAN-001", "Caneca", 0, 5)]
    [InlineData("CAN-001", "Caneca", 30, -1)]
    public void Create_RejectsInvalidData(string sku, string name, decimal price, int stock)
    {
        Assert.Throws<DomainException>(() => Product.Create(sku, name, Brl(price), stock));
    }

    [Fact]
    public void ChangePrice_RejectsZero()
    {
        var product = Product.Create("CAN-001", "Caneca", Brl(30m), 5);

        Assert.Throws<DomainException>(() => product.ChangePrice(Money.Zero("BRL")));
    }
}
