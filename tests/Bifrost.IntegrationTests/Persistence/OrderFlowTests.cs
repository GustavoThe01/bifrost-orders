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
}
