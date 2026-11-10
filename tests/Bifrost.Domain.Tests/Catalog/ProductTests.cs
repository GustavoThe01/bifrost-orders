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

    [Fact]
    public void Reserve_TakesFromTheStock_AndChangesTheVersion()
    {
        var product = Product.Create("CAN-001", "Caneca", Brl(30m), 5);

        product.Reserve(3);

        Assert.Equal(2, product.StockQuantity);
        Assert.Equal(1, product.Version);
    }

    [Fact]
    public void Reserve_MoreThanAvailable_ChangesNothing()
    {
        var product = Product.Create("CAN-001", "Caneca", Brl(30m), 2);

        var exception = Assert.Throws<DomainException>(() => product.Reserve(3));

        Assert.Contains("disponível 2, pedido 3", exception.Message, StringComparison.Ordinal);
        Assert.Equal((2, 0), (product.StockQuantity, product.Version));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ReserveAndRelease_RequireAPositiveQuantity(int quantity)
    {
        var product = Product.Create("CAN-001", "Caneca", Brl(30m), 2);

        Assert.Throws<DomainException>(() => product.Reserve(quantity));
        Assert.Throws<DomainException>(() => product.Release(quantity));
    }

    [Fact]
    public void Release_GivesTheStockBack()
    {
        var product = Product.Create("CAN-001", "Caneca", Brl(30m), 2);
        product.Reserve(2);

        product.Release(2);

        Assert.Equal((2, 2), (product.StockQuantity, product.Version));
    }
}
