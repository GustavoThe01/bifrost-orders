using Bifrost.Application;
using Bifrost.Application.Messaging;
using Bifrost.Application.Orders;
using Bifrost.Infrastructure;
using Bifrost.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bifrost.IntegrationTests.Persistence;

/// <summary>Fluxo completo sem HTTP: dispatcher, pipeline, handlers, EF Core e SQLite com a migration real.</summary>
public sealed class OrderFlowTests : IAsyncLifetime
{
    private readonly string _connectionString = $"Data Source=flow-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly CancellationToken _token = TestContext.Current.CancellationToken;
    private SqliteConnection _keepAlive = null!;
    private ServiceProvider _provider = null!;

    public async ValueTask InitializeAsync()
    {
        // Um banco em memória só existe enquanto houver uma conexão aberta com ele.
        _keepAlive = new SqliteConnection(_connectionString);
        await _keepAlive.OpenAsync(_token);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(_connectionString);
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        await using var scope = _provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<BifrostDbContext>().Database.MigrateAsync(_token);
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _keepAlive.DisposeAsync();
    }

    private async Task<T> InNewScope<T>(Func<IDispatcher, Task<T>> action)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IDispatcher>());
    }

    [Fact]
    public async Task ListProducts_ReturnsTheSeededCatalog()
    {
        var products = await InNewScope(d => d.QueryAsync(new ListProductsQuery(), _token));

        Assert.Equal(5, products.Count);
        Assert.Contains(products, p => p is { Sku: "CAN-001", Price: 39.90m });
    }

    [Fact]
    public async Task Order_GoesFromCreationToCancellation_AcrossScopes()
    {
        var products = await InNewScope(d => d.QueryAsync(new ListProductsQuery(), _token));
        var mug = products.Single(p => p.Sku == "CAN-001");
        var address = new ShippingAddressInput("Av. Beira Mar", "100", "Fortaleza", "CE", "60165-121");

        var id = await InNewScope(d => d.SendAsync(
            new CreateOrderCommand(Guid.NewGuid(), address, "BRL", [new OrderItemInput(mug.Id, 2)]),
            _token));

        var created = await InNewScope(d => d.QueryAsync(new GetOrderByIdQuery(id), _token));
        Assert.Equal("Pending", created.Status);
        Assert.Equal(79.80m, created.Total);
        Assert.Equal("Caneca de cerâmica", Assert.Single(created.Items).ProductName);

        await InNewScope(d => d.SendAsync(new ConfirmOrderCommand(id), _token));
        await InNewScope(d => d.SendAsync(new CancelOrderCommand(id, "Cliente desistiu"), _token));

        var cancelled = await InNewScope(d => d.QueryAsync(new GetOrderByIdQuery(id), _token));
        Assert.Equal("Cancelled", cancelled.Status);
    }

    [Fact]
    public async Task ListOrders_WithDapper_FiltersSumsAndPaginates()
    {
        var products = await InNewScope(d => d.QueryAsync(new ListProductsQuery(), _token));
        var mug = products.Single(p => p.Sku == "CAN-001");
        var lamp = products.Single(p => p.Sku == "LUM-001");
        var address = new ShippingAddressInput("Av. Beira Mar", "100", "Fortaleza", "CE", "60165121");
        var ana = Guid.NewGuid();
        var bruno = Guid.NewGuid();

        var first = await InNewScope(d => d.SendAsync(
            new CreateOrderCommand(ana, address, "BRL", [new OrderItemInput(mug.Id, 2), new OrderItemInput(lamp.Id, 1)]),
            _token));
        var second = await InNewScope(d => d.SendAsync(
            new CreateOrderCommand(ana, address, "BRL", [new OrderItemInput(mug.Id, 1)]), _token));
        await InNewScope(d => d.SendAsync(
            new CreateOrderCommand(bruno, address, "BRL", [new OrderItemInput(lamp.Id, 3)]), _token));
        await InNewScope(d => d.SendAsync(new ConfirmOrderCommand(first), _token));

        var anasFirstPage = await InNewScope(d => d.QueryAsync(new ListOrdersQuery(ana, PageSize: 1), _token));
        var anasSecondPage = await InNewScope(d => d.QueryAsync(new ListOrdersQuery(ana, Page: 2, PageSize: 1), _token));
        var confirmed = await InNewScope(d => d.QueryAsync(new ListOrdersQuery(Status: "Confirmed"), _token));
        var everyone = await InNewScope(d => d.QueryAsync(new ListOrdersQuery(), _token));

        // UUID v7: o pedido mais novo vem primeiro.
        Assert.Equal(second, Assert.Single(anasFirstPage.Items).Id);
        Assert.Equal(2, anasFirstPage.TotalItems);
        Assert.True(anasFirstPage.HasNextPage);

        var older = Assert.Single(anasSecondPage.Items);
        Assert.Equal(first, older.Id);
        Assert.Equal(209.30m, older.Total);
        Assert.Equal(2, older.ItemCount);
        Assert.Equal(ana, older.CustomerId);

        Assert.Equal(first, Assert.Single(confirmed.Items).Id);
        Assert.Equal(3, everyone.TotalItems);
        Assert.Equal(388.50m, everyone.Items[0].Total);
    }

    [Fact]
    public async Task GetOrderById_WithDapper_ReturnsItemsAndTotal()
    {
        var products = await InNewScope(d => d.QueryAsync(new ListProductsQuery(), _token));
        var address = new ShippingAddressInput("Av. Beira Mar", "100", "Fortaleza", "CE", "60165121");
        var items = products.Select(p => new OrderItemInput(p.Id, 1)).ToList();

        var id = await InNewScope(d => d.SendAsync(new CreateOrderCommand(Guid.NewGuid(), address, "BRL", items), _token));
        var order = await InNewScope(d => d.QueryAsync(new GetOrderByIdQuery(id), _token));

        Assert.Equal(5, order.Items.Count);
        Assert.Equal(products.Sum(p => p.Price), order.Total);
        Assert.Equal(order.Items.OrderBy(i => i.ProductName, StringComparer.Ordinal).Select(i => i.ProductName),
            order.Items.Select(i => i.ProductName));
    }
}
