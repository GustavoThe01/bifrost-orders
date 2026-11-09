using Bifrost.Application.Abstractions;
using Bifrost.Application.Catalog;
using Bifrost.Application.Messaging;
using Bifrost.Application.Orders;
using Bifrost.Application.Results;
using Bifrost.Domain.Catalog;
using Bifrost.Domain.Common;
using Bifrost.Domain.Orders;
using Bifrost.Domain.ValueObjects;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Bifrost.Application.Tests.Orders;

/// <summary>
/// Cada handler sozinho, sem dispatcher e sem decorators: só a regra do caso de uso e as portas simuladas.
/// O <see cref="OrderPipelineTests"/> cobre o caminho completo; aqui o foco é o que cada handler decide.
/// </summary>
public sealed class HandlerTests
{
    private static readonly ShippingAddressInput Fortaleza = new("Av. Beira Mar", "100", "Fortaleza", "CE", "60165121");

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IOrderQueries _queries = Substitute.For<IOrderQueries>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly CancellationToken _token = TestContext.Current.CancellationToken;

    private static Money Brl(decimal amount) => Money.Create(amount, "BRL");

    private void Catalog(params Product[] products) =>
        _products.GetByIdsAsync(Arg.Any<IReadOnlyCollection<ProductId>>(), Arg.Any<CancellationToken>()).Returns(products);

    private Order StoredOrder()
    {
        var order = Order.Create(CustomerId.New(), Address.Create("Rua A", "1", "Fortaleza", "CE", "60165121"), "BRL");
        order.AddItem(ProductId.New(), "Caneca", Brl(30m), 1);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        return order;
    }

    [Fact]
    public async Task CreateProduct_RepeatedSku_IsAConflict_AndAddsNothing()
    {
        _products.SkuExistsAsync("CAN-001", Arg.Any<CancellationToken>()).Returns(true);
        var handler = new CreateProductHandler(_products, _unitOfWork);

        var result = await handler.HandleAsync(new CreateProductCommand("CAN-001", "Caneca", 39.90m, "BRL", 10), _token);

        Assert.Equal(("product.sku_taken", ErrorType.Conflict), (result.Error?.Code, result.Error?.Type));
        _products.DidNotReceive().Add(Arg.Any<Product>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateProduct_NewSku_IsAddedAndSaved()
    {
        var handler = new CreateProductHandler(_products, _unitOfWork);

        var result = await handler.HandleAsync(new CreateProductCommand("NOV-001", "Novidade", 10.50m, "BRL", 3), _token);

        _products.Received(1).Add(Arg.Is<Product>(p => p.Id.Value == result.Value && p.Price == Brl(10.50m)));
        await _unitOfWork.Received(1).SaveChangesAsync(_token);
    }

    [Fact]
    public async Task CreateOrder_RepeatedProduct_IsMergedIntoOneLine_WithTheCatalogPrice()
    {
        var mug = Product.Create("CAN-001", "Caneca", Brl(39.90m), 10);
        Catalog(mug);
        Order? added = null;
        _orders.Add(Arg.Do<Order>(order => added = order));
        var handler = new CreateOrderHandler(_products, _orders, _unitOfWork);

        await handler.HandleAsync(
            new CreateOrderCommand(Guid.NewGuid(), Fortaleza, "BRL", [new(mug.Id.Value, 1), new(mug.Id.Value, 2)]), _token);

        var line = Assert.Single(added!.Items);
        Assert.Equal(3, line.Quantity);
        Assert.Equal(Brl(119.70m), added.Total);
        await _products.Received(1).GetByIdsAsync(
            Arg.Is<IReadOnlyCollection<ProductId>>(ids => ids.Count == 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateOrder_UnknownProduct_IsNotFound_AndAddsNothing()
    {
        Catalog();
        var handler = new CreateOrderHandler(_products, _orders, _unitOfWork);

        var result = await handler.HandleAsync(
            new CreateOrderCommand(Guid.NewGuid(), Fortaleza, "BRL", [new(Guid.NewGuid(), 1)]), _token);

        Assert.Equal(ErrorType.NotFound, result.Error?.Type);
        _orders.DidNotReceive().Add(Arg.Any<Order>());
    }

    [Fact]
    public async Task CreateOrder_RuleViolatedInTheDomain_EscapesTheHandlerAsException()
    {
        // Sem o pipeline, a exceção do domínio sai do handler. Quem a converte em Result é o decorator (ADR 0004).
        Catalog(Product.Create("CAN-001", "Caneca", Brl(39.90m), 10));
        var handler = new CreateOrderHandler(_products, _orders, _unitOfWork);
        var mugId = (await _products.GetByIdsAsync([], _token))[0].Id.Value;

        await Assert.ThrowsAsync<DomainException>(() => handler.HandleAsync(
            new CreateOrderCommand(Guid.NewGuid(), Fortaleza, "USD", [new(mugId, 1)]), _token));

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmOrder_UnknownOrder_IsNotFound()
    {
        var result = await new ConfirmOrderHandler(_orders, _unitOfWork).HandleAsync(new ConfirmOrderCommand(Guid.NewGuid()), _token);

        Assert.Equal("order.not_found", result.Error?.Code);
    }

    [Fact]
    public async Task CancelOrder_KeepsTheReason_AndSaves()
    {
        var order = StoredOrder();

        var result = await new CancelOrderHandler(_orders, _unitOfWork)
            .HandleAsync(new CancelOrderCommand(order.Id.Value, "  Desistência  "), _token);

        Assert.Equal(OrderStatus.Cancelled, result.Value);
        Assert.Equal("Desistência", order.CancellationReason);
        await _unitOfWork.Received(1).SaveChangesAsync(_token);
    }

    [Fact]
    public async Task ListOrders_PassesTheFilterAsTyped_Values()
    {
        var customer = Guid.NewGuid();
        await new ListOrdersHandler(_queries).HandleAsync(new ListOrdersQuery(customer, "SHIPPED", 3, 15), _token);

        await _queries.Received(1).ListOrdersAsync(new OrderListFilter(customer, OrderStatus.Shipped), 3, 15, _token);
    }

    [Fact]
    public async Task BusinessRuleDecorator_ConvertsOnlyDomainExceptions()
    {
        var inner = Substitute.For<ICommandHandler<ConfirmOrderCommand, Result<OrderStatus>>>();
        var command = new ConfirmOrderCommand(Guid.NewGuid());
        var decorator = new BusinessRuleCommandDecorator<ConfirmOrderCommand, Result<OrderStatus>>(inner);

        inner.HandleAsync(command, _token).ThrowsAsync(new DomainException("Regra quebrada."));
        var converted = await decorator.HandleAsync(command, _token);

        inner.HandleAsync(command, _token).ThrowsAsync(new TimeoutException("banco fora do ar"));
        await Assert.ThrowsAsync<TimeoutException>(() => decorator.HandleAsync(command, _token));

        Assert.Equal((ErrorType.BusinessRule, "Regra quebrada."), (converted.Error?.Type, converted.Error?.Message));
    }
}
