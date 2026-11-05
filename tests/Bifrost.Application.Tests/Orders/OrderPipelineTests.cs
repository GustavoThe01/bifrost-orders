using Bifrost.Application.Abstractions;
using Bifrost.Application.Messaging;
using Bifrost.Application.Orders;
using Bifrost.Domain.Catalog;
using Bifrost.Domain.Common;
using Bifrost.Domain.Orders;
using Bifrost.Domain.ValueObjects;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Bifrost.Application.Tests.Orders;

/// <summary>Testa pelo dispatcher, como a API vai usar: o pipeline inteiro participa de cada teste.</summary>
public sealed class OrderPipelineTests : IAsyncDisposable
{
    private static readonly ShippingAddressInput Fortaleza = new("Av. Beira Mar", "100", "Fortaleza", "CE", "60165121");

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IOrderQueries _queries = Substitute.For<IOrderQueries>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ServiceProvider _provider;
    private readonly AsyncServiceScope _scope;
    private readonly IDispatcher _dispatcher;
    private readonly CancellationToken _token = TestContext.Current.CancellationToken;

    public OrderPipelineTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddSingleton(_orders);
        services.AddSingleton(_products);
        services.AddSingleton(_queries);
        services.AddSingleton(_unitOfWork);
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        _scope = _provider.CreateAsyncScope();
        _dispatcher = _scope.ServiceProvider.GetRequiredService<IDispatcher>();
    }

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
    }

    private Product CatalogProduct(string sku, decimal price)
    {
        var product = Product.Create(sku, "Produto " + sku, Money.Create(price, "BRL"), 10);
        _products.GetByIdsAsync(Arg.Any<IReadOnlyCollection<ProductId>>(), Arg.Any<CancellationToken>())
            .Returns([product]);
        return product;
    }

    private Order PendingOrder()
    {
        var product = Product.Create("CAN-001", "Caneca", Money.Create(30m, "BRL"), 10);
        var order = Order.Create(CustomerId.New(), Address.Create("Rua A", "1", "Fortaleza", "CE", "60165121"), "BRL");
        order.AddItem(product.Id, product.Name, product.Price, 1);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        return order;
    }

    [Fact]
    public async Task CreateOrder_UsesThePriceFromTheCatalog_AndSaves()
    {
        var product = CatalogProduct("CAN-001", 39.90m);
        var command = new CreateOrderCommand(Guid.NewGuid(), Fortaleza, "BRL", [new OrderItemInput(product.Id.Value, 2)]);

        var id = await _dispatcher.SendAsync(command, _token);

        _orders.Received(1).Add(Arg.Is<Order>(o => o.Id.Value == id && o.Total == Money.Create(79.80m, "BRL")));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateOrder_UnknownProduct_IsNotFound_AndSavesNothing()
    {
        _products.GetByIdsAsync(Arg.Any<IReadOnlyCollection<ProductId>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Product>());
        var command = new CreateOrderCommand(Guid.NewGuid(), Fortaleza, "BRL", [new OrderItemInput(Guid.NewGuid(), 1)]);

        await Assert.ThrowsAsync<NotFoundException>(() => _dispatcher.SendAsync(command, _token));

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidCommand_StopsAtValidation_BeforeTheHandler()
    {
        var command = new CreateOrderCommand(Guid.Empty, Fortaleza, "BRL", []);

        var exception = await Assert.ThrowsAsync<ValidationException>(() => _dispatcher.SendAsync(command, _token));

        Assert.Contains(exception.Errors, e => e.PropertyName == nameof(CreateOrderCommand.CustomerId));
        Assert.Contains(exception.Errors, e => e.PropertyName == nameof(CreateOrderCommand.Items));
        await _products.DidNotReceive().GetByIdsAsync(Arg.Any<IReadOnlyCollection<ProductId>>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task ItemQuantity_OutOfRange_FailsValidation(int quantity)
    {
        var command = new CreateOrderCommand(Guid.NewGuid(), Fortaleza, "BRL", [new OrderItemInput(Guid.NewGuid(), quantity)]);

        await Assert.ThrowsAsync<ValidationException>(() => _dispatcher.SendAsync(command, _token));
    }

    [Fact]
    public async Task ConfirmOrder_ChangesStatus_AndSaves()
    {
        var order = PendingOrder();

        var status = await _dispatcher.SendAsync(new ConfirmOrderCommand(order.Id.Value), _token);

        Assert.Equal(OrderStatus.Confirmed, status);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmOrder_Unknown_IsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _dispatcher.SendAsync(new ConfirmOrderCommand(Guid.NewGuid()), _token));
    }

    [Fact]
    public async Task CancelOrder_WithoutReason_FailsValidation()
    {
        var order = PendingOrder();

        await Assert.ThrowsAsync<ValidationException>(
            () => _dispatcher.SendAsync(new CancelOrderCommand(order.Id.Value, " "), _token));

        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public async Task DomainRule_ViolatedInTheHandler_SavesNothing()
    {
        var order = PendingOrder();
        order.Confirm();
        order.MarkAsPaid();

        await Assert.ThrowsAsync<DomainException>(
            () => _dispatcher.SendAsync(new CancelOrderCommand(order.Id.Value, "Desistência"), _token));

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOrderById_ReturnsTheDto()
    {
        var id = Guid.NewGuid();
        var dto = new OrderDto(id, Guid.NewGuid(), "Pending", "BRL", 10m, []);
        _queries.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(dto);

        Assert.Same(dto, await _dispatcher.QueryAsync(new GetOrderByIdQuery(id), _token));
    }

    [Fact]
    public async Task GetOrderById_Unknown_IsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _dispatcher.QueryAsync(new GetOrderByIdQuery(Guid.NewGuid()), _token));
    }

    [Fact]
    public async Task ListProducts_GoesThroughTheQueryPipeline()
    {
        _queries.ListProductsAsync(Arg.Any<CancellationToken>()).Returns(new List<ProductDto>());

        Assert.Empty(await _dispatcher.QueryAsync(new ListProductsQuery(), _token));
    }

    [Fact]
    public async Task UnregisteredCommand_FailsWithClearError()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _dispatcher.SendAsync(new NotRegistered(), _token));
    }

    private sealed record NotRegistered : ICommand<int>;
}
