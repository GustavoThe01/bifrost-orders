using Bifrost.Domain.Common;
using Bifrost.Domain.Orders;
using Bifrost.Domain.ValueObjects;

namespace Bifrost.Domain.Tests.Orders;

public class OrderTests
{
    private static readonly Address Fortaleza = Address.Create("Av. Beira Mar", "100", "Fortaleza", "CE", "60165121");
    private static readonly ProductId Mug = ProductId.New();
    private static readonly ProductId Shirt = ProductId.New();

    private static Money Brl(decimal amount) => Money.Create(amount, "BRL");

    private static Order NewOrder() => Order.Create(CustomerId.New(), Fortaleza, "BRL");

    private static Order ConfirmedOrder()
    {
        var order = NewOrder();
        order.AddItem(Mug, "Caneca", Brl(30m), 2);
        order.Confirm();
        return order;
    }

    [Fact]
    public void Create_StartsPendingAndRaisesOrderCreated()
    {
        var order = NewOrder();

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.True(order.Total.IsZero);
        var created = Assert.IsType<OrderCreated>(Assert.Single(order.DomainEvents));
        Assert.Equal(order.Id, created.OrderId);
    }

    [Fact]
    public void AddItem_CalculatesTotal()
    {
        var order = NewOrder();

        order.AddItem(Mug, "Caneca", Brl(30m), 2);
        order.AddItem(Shirt, "Camiseta", Brl(80m), 1);

        Assert.Equal(2, order.Items.Count);
        Assert.Equal(Brl(140m), order.Total);
    }

    [Fact]
    public void AddItem_SameProduct_SumsQuantity()
    {
        var order = NewOrder();

        order.AddItem(Mug, "Caneca", Brl(30m), 2);
        order.AddItem(Mug, "Caneca", Brl(30m), 1);

        var item = Assert.Single(order.Items);
        Assert.Equal(3, item.Quantity);
    }

    [Fact]
    public void AddItem_SameProductWithDifferentPrice_IsRejected()
    {
        var order = NewOrder();
        order.AddItem(Mug, "Caneca", Brl(30m), 1);

        Assert.Throws<DomainException>(() => order.AddItem(Mug, "Caneca", Brl(32m), 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void AddItem_RejectsInvalidQuantity(int quantity)
    {
        Assert.Throws<DomainException>(() => NewOrder().AddItem(Mug, "Caneca", Brl(30m), quantity));
    }

    [Fact]
    public void AddItem_RejectsOtherCurrency()
    {
        Assert.Throws<DomainException>(() => NewOrder().AddItem(Mug, "Caneca", Money.Create(5m, "USD"), 1));
    }

    [Fact]
    public void RemoveItem_RemovesFromTotal()
    {
        var order = NewOrder();
        order.AddItem(Mug, "Caneca", Brl(30m), 2);
        order.AddItem(Shirt, "Camiseta", Brl(80m), 1);

        order.RemoveItem(Mug);

        Assert.Equal(Brl(80m), order.Total);
    }

    [Fact]
    public void Confirm_WithoutItems_IsRejected()
    {
        Assert.Throws<DomainException>(NewOrder().Confirm);
    }

    [Fact]
    public void Confirm_RaisesEventWithTotal_AndFreezesItems()
    {
        var order = ConfirmedOrder();

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        var confirmed = Assert.IsType<OrderConfirmed>(order.DomainEvents.Last());
        Assert.Equal(Brl(60m), confirmed.Total);
        Assert.Throws<DomainException>(() => order.AddItem(Shirt, "Camiseta", Brl(80m), 1));
    }

    [Fact]
    public void HappyPath_GoesUntilDelivered()
    {
        var order = ConfirmedOrder();

        order.MarkAsPaid();
        order.Ship();
        order.Deliver();

        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Collection(
            order.DomainEvents,
            e => Assert.IsType<OrderCreated>(e),
            e => Assert.IsType<OrderConfirmed>(e),
            e => Assert.IsType<OrderPaid>(e),
            e => Assert.IsType<OrderShipped>(e),
            e => Assert.IsType<OrderDelivered>(e));
    }

    [Fact]
    public void Steps_CannotBeSkipped()
    {
        var order = NewOrder();
        order.AddItem(Mug, "Caneca", Brl(30m), 1);

        Assert.Throws<DomainException>(order.MarkAsPaid);
        Assert.Throws<DomainException>(order.Ship);
        Assert.Throws<DomainException>(order.Deliver);
    }

    [Fact]
    public void Cancel_ConfirmedOrder_KeepsReasonAndPreviousStatus()
    {
        var order = ConfirmedOrder();

        order.Cancel("  Cliente desistiu  ");

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal("Cliente desistiu", order.CancellationReason);
        var cancelled = Assert.IsType<OrderCancelled>(order.DomainEvents.Last());
        Assert.Equal(OrderStatus.Confirmed, cancelled.PreviousStatus);
    }

    [Fact]
    public void Cancel_AfterPayment_IsRejected()
    {
        var order = ConfirmedOrder();
        order.MarkAsPaid();

        Assert.Throws<DomainException>(() => order.Cancel("Desistência"));
    }

    [Fact]
    public void Cancel_RequiresReason()
    {
        Assert.Throws<DomainException>(() => NewOrder().Cancel(" "));
    }

    [Fact]
    public void ClearDomainEvents_EmptiesTheList()
    {
        var order = ConfirmedOrder();

        order.ClearDomainEvents();

        Assert.Empty(order.DomainEvents);
    }
}
