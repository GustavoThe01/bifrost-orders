using Bifrost.Application;
using Bifrost.Application.Abstractions;
using Bifrost.Application.Catalog;
using Bifrost.Application.Messaging;
using Bifrost.Application.Orders;
using Bifrost.Application.Results;
using Bifrost.Domain.Orders;
using Bifrost.Infrastructure;
using Bifrost.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bifrost.IntegrationTests.Persistence;

/// <summary>
/// Pedidos disputando o mesmo estoque, num SQLite em arquivo e com uma conexão por operação,
/// como acontece com requisições simultâneas na API.
/// </summary>
public sealed class StockConcurrencyTests : IAsyncLifetime
{
    private static readonly ShippingAddressInput Address = new("Av. Beira Mar", "100", "Fortaleza", "CE", "60165121");

    private readonly string _file = Path.Combine(Path.GetTempPath(), $"bifrost-estoque-{Guid.NewGuid():N}.db");
    private readonly CancellationToken _token = TestContext.Current.CancellationToken;
    private ServiceProvider _provider = null!;

    public async ValueTask InitializeAsync()
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = _file, Pooling = false }.ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(connectionString);
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        await using var scope = _provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<BifrostDbContext>().Database.MigrateAsync(_token);
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        File.Delete(_file);
    }

    private async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private Task<Result<T>> Send<T>(ICommand<Result<T>> command) =>
        InScope(s => s.GetRequiredService<IDispatcher>().SendAsync(command, _token));

    private async Task<Guid> ProductWithStockAsync(int stock) =>
        (await Send(new CreateProductCommand($"ULT-{Random.Shared.Next(100, 999)}", "Último item", 50m, "BRL", stock))).Value;

    private async Task<int> StockOfAsync(Guid productId) =>
        await InScope(async s => (await s.GetRequiredService<BifrostDbContext>().Products.AsNoTracking()
            .SingleAsync(p => p.Id == new ProductId(productId), _token)).StockQuantity);

    [Fact]
    public async Task TenOrders_ForTheLastUnit_OnlyOneIsConfirmed()
    {
        var product = await ProductWithStockAsync(1);
        var orders = new List<Guid>();
        for (var i = 0; i < 10; i++)
        {
            orders.Add((await Send(new CreateOrderCommand(Guid.NewGuid(), Address, "BRL", [new OrderItemInput(product, 1)]))).Value);
        }

        var results = await Task.WhenAll(orders.Select(id => Task.Run(() => Send(new ConfirmOrderCommand(id)), _token)));

        // Um confirma. Os outros perdem de um jeito ou de outro: ou leram o saldo já zerado (422),
        // ou leram junto com o vencedor e o token de concorrência barrou a gravação (409).
        Assert.Single(results, r => r.IsSuccess);
        Assert.All(results.Where(r => r.IsFailure), r =>
            Assert.Contains(r.Error!.Type, new[] { ErrorType.BusinessRule, ErrorType.Conflict }));
        Assert.Equal(0, await StockOfAsync(product));
    }

    [Fact]
    public async Task TwoReadsOfTheSameProduct_TheSecondWriteIsRejected()
    {
        var productId = new ProductId(await ProductWithStockAsync(5));

        await using var first = _provider.CreateAsyncScope();
        await using var second = _provider.CreateAsyncScope();
        var a = (await first.ServiceProvider.GetRequiredService<IProductRepository>().GetByIdsAsync([productId], _token))[0];
        var b = (await second.ServiceProvider.GetRequiredService<IProductRepository>().GetByIdsAsync([productId], _token))[0];

        a.Reserve(3);
        b.Reserve(4);
        await first.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(_token);

        // Sem o token, a segunda gravação deixaria o saldo em 1 e a primeira reserva sumiria (lost update).
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => second.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(_token));
        Assert.Equal(2, await StockOfAsync(productId.Value));
    }

    [Fact]
    public async Task ConfirmReserves_AndCancellingAConfirmedOrderGivesTheStockBack()
    {
        var product = await ProductWithStockAsync(10);
        var order = (await Send(new CreateOrderCommand(Guid.NewGuid(), Address, "BRL", [new OrderItemInput(product, 4)]))).Value;

        await Send(new ConfirmOrderCommand(order));
        Assert.Equal(6, await StockOfAsync(product));

        await Send(new CancelOrderCommand(order, "Cliente desistiu"));
        Assert.Equal(10, await StockOfAsync(product));
    }

    [Fact]
    public async Task ConfirmWithoutStock_IsABusinessRule_AndNothingChanges()
    {
        var product = await ProductWithStockAsync(2);
        var order = (await Send(new CreateOrderCommand(Guid.NewGuid(), Address, "BRL", [new OrderItemInput(product, 3)]))).Value;

        var result = await Send(new ConfirmOrderCommand(order));

        Assert.Equal(ErrorType.BusinessRule, result.Error?.Type);
        Assert.Contains("Estoque insuficiente", result.Error!.Message, StringComparison.Ordinal);
        Assert.Equal(2, await StockOfAsync(product));
        var status = await InScope(s => s.GetRequiredService<IDispatcher>().QueryAsync(new GetOrderByIdQuery(order), _token));
        Assert.Equal("Pending", status.Value.Status);
    }
}
