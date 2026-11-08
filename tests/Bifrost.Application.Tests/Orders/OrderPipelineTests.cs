using Bifrost.Application.Abstractions;
using Bifrost.Application.Messaging;
using Bifrost.Application.Orders;
using Bifrost.Application.Results;
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

        var id = (await _dispatcher.SendAsync(command, _token)).Value;

        _orders.Received(1).Add(Arg.Is<Order>(o => o.Id.Value == id && o.Total == Money.Create(79.80m, "BRL")));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateOrder_UnknownProduct_IsNotFound_AndSavesNothing()
    {
        _products.GetByIdsAsync(Arg.Any<IReadOnlyCollection<ProductId>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Product>());
        var command = new CreateOrderCommand(Guid.NewGuid(), Fortaleza, "BRL", [new OrderItemInput(Guid.NewGuid(), 1)]);

        var result = await _dispatcher.SendAsync(command, _token);

        Assert.Equal(ErrorType.NotFound, result.Error?.Type);
        Assert.Equal("product.not_found", result.Error?.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidCommand_StopsAtValidation_BeforeTheHandler()
    {
        var command = new CreateOrderCommand(Guid.Empty, Fortaleza, "BRL", []);

        var result = await _dispatcher.SendAsync(command, _token);

        Assert.Equal(ErrorType.Validation, result.Error?.Type);
        Assert.Contains(result.Error!.Fields, e => e.Field == nameof(CreateOrderCommand.CustomerId));
        Assert.Contains(result.Error.Fields, e => e.Field == nameof(CreateOrderCommand.Items));
        await _products.DidNotReceive().GetByIdsAsync(Arg.Any<IReadOnlyCollection<ProductId>>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task ItemQuantity_OutOfRange_FailsValidation(int quantity)
    {
        var command = new CreateOrderCommand(Guid.NewGuid(), Fortaleza, "BRL", [new OrderItemInput(Guid.NewGuid(), quantity)]);

        Assert.Equal(ErrorType.Validation, (await _dispatcher.SendAsync(command, _token)).Error?.Type);
    }

    [Fact]
    public async Task ConfirmOrder_ChangesStatus_AndSaves()
    {
        var order = PendingOrder();

        var result = await _dispatcher.SendAsync(new ConfirmOrderCommand(order.Id.Value), _token);

        Assert.Equal(OrderStatus.Confirmed, result.Value);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmOrder_Unknown_IsNotFound()
    {
        var result = await _dispatcher.SendAsync(new ConfirmOrderCommand(Guid.NewGuid()), _token);

        Assert.Equal("order.not_found", result.Error?.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelOrder_WithoutReason_FailsValidation()
    {
        var order = PendingOrder();

        var result = await _dispatcher.SendAsync(new CancelOrderCommand(order.Id.Value, " "), _token);

        Assert.Equal(ErrorType.Validation, result.Error?.Type);

        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public async Task DomainRule_ViolatedInTheHandler_SavesNothing()
    {
        var order = PendingOrder();
        order.Confirm();
        order.MarkAsPaid();

        // O domínio lança DomainException; o pipeline devolve uma falha, sem exceção para quem chamou.
        var result = await _dispatcher.SendAsync(new CancelOrderCommand(order.Id.Value, "Desistência"), _token);

        Assert.Equal(ErrorType.BusinessRule, result.Error?.Type);
        Assert.Contains("Situação atual: Paid", result.Error!.Message, StringComparison.Ordinal);

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOrderById_ReturnsTheDto()
    {
        var id = Guid.NewGuid();
        var dto = new OrderDto(id, Guid.NewGuid(), "Pending", "BRL", 10m, []);
        _queries.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(dto);

        Assert.Same(dto, (await _dispatcher.QueryAsync(new GetOrderByIdQuery(id), _token)).Value);
    }

    [Fact]
    public async Task GetOrderById_Unknown_IsNotFound()
    {
        var result = await _dispatcher.QueryAsync(new GetOrderByIdQuery(Guid.NewGuid()), _token);

        Assert.Equal(ErrorType.NotFound, result.Error?.Type);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public async Task ListProducts_GoesThroughTheQueryPipeline()
    {
        _queries.ListProductsAsync(Arg.Any<CancellationToken>()).Returns(new List<ProductDto>());

        Assert.Empty((await _dispatcher.QueryAsync(new ListProductsQuery(), _token)).Value);
    }

    [Fact]
    public async Task ListOrders_ConvertsTheStatus_AndPassesThePage()
    {
        var customer = Guid.NewGuid();
        var page = new PagedResult<OrderSummaryDto>([], 2, 10, 0);
        _queries.ListOrdersAsync(new OrderListFilter(customer, OrderStatus.Confirmed), 2, 10, Arg.Any<CancellationToken>())
            .Returns(page);

        var result = await _dispatcher.QueryAsync(new ListOrdersQuery(customer, "confirmed", 2, 10), _token);

        Assert.Same(page, result.Value);
    }

    [Theory]
    [InlineData(0, 20, null)]
    [InlineData(1, 0, null)]
    [InlineData(1, 101, null)]
    [InlineData(1, 20, "Arquivado")]
    [InlineData(1, 20, "3")]
    public async Task ListOrders_InvalidQuery_StopsAtValidation(int page, int pageSize, string? status)
    {
        var result = await _dispatcher.QueryAsync(new ListOrdersQuery(null, status, page, pageSize), _token);

        Assert.Equal(ErrorType.Validation, result.Error?.Type);

        await _queries.DidNotReceive().ListOrdersAsync(
            Arg.Any<OrderListFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnexpectedException_IsNotTurnedIntoResult()
    {
        var order = PendingOrder();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(new TimeoutException("banco fora do ar")));

        await Assert.ThrowsAsync<TimeoutException>(() => _dispatcher.SendAsync(new ConfirmOrderCommand(order.Id.Value), _token));
    }

    [Fact]
    public void Result_HoldsAValueOrAnError_NeverBoth()
    {
        Result<int> success = 42;
        Result<int> failure = Error.Conflict("x.conflict", "Conflito.");

        Assert.Equal("ok 42", success.Match(v => $"ok {v}", e => e.Code));
        Assert.Equal("x.conflict", failure.Match(v => $"ok {v}", e => e.Code));
        Assert.True(failure.IsFailure);
        Assert.Null(success.Error);
    }

    [Fact]
    public void PagedResult_CalculatesThePages()
    {
        var page = new PagedResult<int>([1, 2], 1, 2, 5);

        Assert.Equal(3, page.TotalPages);
        Assert.True(page.HasNextPage);
        Assert.False(new PagedResult<int>([], 1, 20, 0).HasNextPage);
    }

    [Fact]
    public async Task UnregisteredCommand_FailsWithClearError()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _dispatcher.SendAsync(new NotRegistered(), _token));
    }

    private sealed record NotRegistered : ICommand<int>;
}
