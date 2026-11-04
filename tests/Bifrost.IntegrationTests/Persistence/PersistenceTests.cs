using Bifrost.Domain.Catalog;
using Bifrost.Domain.Orders;
using Bifrost.Domain.ValueObjects;
using Bifrost.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Bifrost.IntegrationTests.Persistence;

/// <summary>
/// Testa o mapeamento contra um SQLite real, em memória, com a migration de verdade.
/// Cada contexto novo garante que os dados vieram do banco, e não do cache do EF Core.
/// </summary>
public sealed class PersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<BifrostDbContext> _options;

    public PersistenceTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<BifrostDbContext>().UseSqlite(_connection).Options;

        using var context = NewContext();
        context.Database.Migrate();
    }

    public void Dispose() => _connection.Dispose();

    private BifrostDbContext NewContext() => new(_options);

    private static Money Brl(decimal amount) => Money.Create(amount, "BRL");

    private static Address Fortaleza() => Address.Create("Av. Beira Mar", "100", "Fortaleza", "CE", "60165121");

    private Product SavedProduct(string sku, decimal price = 30m)
    {
        var product = Product.Create(sku, "Produto " + sku, Brl(price), 10);
        using var context = NewContext();
        context.Products.Add(product);
        context.SaveChanges();
        return product;
    }

    [Fact]
    public void Migration_HasNoPendingModelChanges()
    {
        using var context = NewContext();

        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Product_RoundTrip_KeepsPriceExact()
    {
        var product = SavedProduct("CAN-001", 39.90m);

        using var context = NewContext();
        var found = context.Products.Single(p => p.Id == product.Id);

        Assert.Equal("CAN-001", found.Sku);
        Assert.Equal(Brl(39.90m), found.Price);
        Assert.Equal(10, found.StockQuantity);
    }

    [Fact]
    public void Money_IsStoredInCents()
    {
        SavedProduct("CAN-002", 39.90m);

        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT price_cents, typeof(price_cents) FROM products WHERE sku = 'CAN-002'";
        using var reader = command.ExecuteReader();
        reader.Read();

        Assert.Equal(3990L, reader.GetInt64(0));
        Assert.Equal("integer", reader.GetString(1));
    }

    [Fact]
    public void Order_RoundTrip_KeepsItemsAddressAndStatus()
    {
        var mug = SavedProduct("CAN-003", 30m);
        var shirt = SavedProduct("CAM-003", 80m);
        var order = Order.Create(CustomerId.New(), Fortaleza(), "BRL");
        order.AddItem(mug.Id, mug.Name, mug.Price, 2);
        order.AddItem(shirt.Id, shirt.Name, shirt.Price, 1);
        order.Confirm();

        using (var context = NewContext())
        {
            context.Orders.Add(order);
            context.SaveChanges();
        }

        using var readContext = NewContext();
        var found = readContext.Orders.Include(o => o.Items).Single(o => o.Id == order.Id);

        Assert.Equal(OrderStatus.Confirmed, found.Status);
        Assert.Equal(order.CustomerId, found.CustomerId);
        Assert.Equal(Fortaleza(), found.ShippingAddress);
        Assert.Equal(2, found.Items.Count);
        Assert.Equal(Brl(140m), found.Total);
        Assert.Empty(found.DomainEvents);
    }

    [Fact]
    public void Order_AddingItemAfterReload_IsPersisted()
    {
        var mug = SavedProduct("CAN-004");
        var shirt = SavedProduct("CAM-004");
        var order = Order.Create(CustomerId.New(), Fortaleza(), "BRL");
        order.AddItem(mug.Id, mug.Name, mug.Price, 1);
        using (var context = NewContext())
        {
            context.Orders.Add(order);
            context.SaveChanges();
        }

        using (var context = NewContext())
        {
            var tracked = context.Orders.Include(o => o.Items).Single(o => o.Id == order.Id);
            tracked.AddItem(shirt.Id, shirt.Name, shirt.Price, 3);
            context.SaveChanges();
        }

        using var readContext = NewContext();
        Assert.Equal(2, readContext.Orders.Include(o => o.Items).Single(o => o.Id == order.Id).Items.Count);
    }

    [Fact]
    public void Status_IsStoredAsText()
    {
        var mug = SavedProduct("CAN-005");
        var order = Order.Create(CustomerId.New(), Fortaleza(), "BRL");
        order.AddItem(mug.Id, mug.Name, mug.Price, 1);
        using (var context = NewContext())
        {
            context.Orders.Add(order);
            context.SaveChanges();
        }

        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT status FROM orders";

        Assert.Equal("Pending", command.ExecuteScalar());
    }

    [Fact]
    public void Sku_MustBeUnique()
    {
        SavedProduct("DUP-001");

        Assert.Throws<DbUpdateException>(() => SavedProduct("DUP-001"));
    }

    [Fact]
    public void Product_InAnOrder_CannotBeDeleted()
    {
        var mug = SavedProduct("CAN-006");
        var order = Order.Create(CustomerId.New(), Fortaleza(), "BRL");
        order.AddItem(mug.Id, mug.Name, mug.Price, 1);
        using (var context = NewContext())
        {
            context.Orders.Add(order);
            context.SaveChanges();
        }

        using var deleteContext = NewContext();
        deleteContext.Products.Remove(deleteContext.Products.Single(p => p.Id == mug.Id));

        Assert.Throws<DbUpdateException>(() => deleteContext.SaveChanges());
    }

    [Fact]
    public void Seed_AddsCatalogOnlyOnce()
    {
        using var context = NewContext();

        CatalogSeed.Seed(context);
        CatalogSeed.Seed(context);

        Assert.Equal(5, context.Products.Count());
    }
}
