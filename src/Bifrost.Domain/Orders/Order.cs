using Bifrost.Domain.Common;
using Bifrost.Domain.ValueObjects;

namespace Bifrost.Domain.Orders;

/// <summary>
/// Pedido de compra. Agregado que controla os itens e o ciclo de vida até a entrega.
/// </summary>
/// <remarks>
/// <code>
/// Pending ──► Confirmed ──► Paid ──► Shipped ──► Delivered
///    │            │
///    └────────────┴──► Cancelled
/// </code>
/// Regras:
/// <list type="bullet">
/// <item>itens só podem ser alterados enquanto o pedido está Pending;</item>
/// <item>o mesmo produto adicionado duas vezes soma a quantidade, em vez de duplicar o item;</item>
/// <item>todos os itens usam a moeda do pedido;</item>
/// <item>não existe pedido confirmado sem itens;</item>
/// <item>depois de pago, o pedido não pode mais ser cancelado (vira devolução, fora do escopo).</item>
/// </list>
/// </remarks>
public sealed class Order : AggregateRoot<OrderId>
{
    private readonly List<OrderItem> _items = [];

    private Order(OrderId id, CustomerId customerId, Address shippingAddress, string currency)
        : base(id)
    {
        CustomerId = customerId;
        ShippingAddress = shippingAddress;
        Currency = currency;
        Status = OrderStatus.Pending;
    }

    public CustomerId CustomerId { get; }

    public Address ShippingAddress { get; }

    public string Currency { get; }

    public OrderStatus Status { get; private set; }

    public string? CancellationReason { get; private set; }

    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    public Money Total => _items.Aggregate(Money.Zero(Currency), (total, item) => total + item.Total);

    public static Order Create(CustomerId customerId, Address shippingAddress, string currency)
    {
        ArgumentNullException.ThrowIfNull(shippingAddress);

        var order = new Order(OrderId.New(), customerId, shippingAddress, Money.Zero(currency).Currency);
        order.Raise(new OrderCreated(order.Id, customerId));
        return order;
    }

    public void AddItem(ProductId productId, string productName, Money unitPrice, int quantity)
    {
        EnsureStatus(OrderStatus.Pending, "alterar os itens");

        if (unitPrice.Currency != Currency)
        {
            throw new DomainException($"O pedido é em {Currency}; o item está em {unitPrice.Currency}.");
        }

        var existing = _items.Find(item => item.ProductId == productId);
        if (existing is null)
        {
            _items.Add(new OrderItem(productId, productName, unitPrice, quantity));
            return;
        }

        if (existing.UnitPrice != unitPrice)
        {
            throw new DomainException("O mesmo produto não pode entrar no pedido com dois preços diferentes.");
        }

        existing.IncreaseQuantity(quantity);
    }

    public void RemoveItem(ProductId productId)
    {
        EnsureStatus(OrderStatus.Pending, "alterar os itens");

        if (_items.RemoveAll(item => item.ProductId == productId) == 0)
        {
            throw new DomainException($"O produto {productId} não está no pedido.");
        }
    }

    public void Confirm()
    {
        EnsureStatus(OrderStatus.Pending, "confirmar");

        if (_items.Count == 0)
        {
            throw new DomainException("Não é possível confirmar um pedido sem itens.");
        }

        Status = OrderStatus.Confirmed;
        Raise(new OrderConfirmed(Id, Total));
    }

    public void MarkAsPaid()
    {
        EnsureStatus(OrderStatus.Confirmed, "registrar o pagamento");
        Status = OrderStatus.Paid;
        Raise(new OrderPaid(Id));
    }

    public void Ship()
    {
        EnsureStatus(OrderStatus.Paid, "enviar");
        Status = OrderStatus.Shipped;
        Raise(new OrderShipped(Id));
    }

    public void Deliver()
    {
        EnsureStatus(OrderStatus.Shipped, "registrar a entrega");
        Status = OrderStatus.Delivered;
        Raise(new OrderDelivered(Id));
    }

    public void Cancel(string reason)
    {
        if (Status is not (OrderStatus.Pending or OrderStatus.Confirmed))
        {
            throw new DomainException($"Só é possível cancelar pedidos pendentes ou confirmados. Situação atual: {Status}.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Informe o motivo do cancelamento.");
        }

        var previousStatus = Status;
        Status = OrderStatus.Cancelled;
        CancellationReason = reason.Trim();
        Raise(new OrderCancelled(Id, previousStatus, CancellationReason));
    }

    private void EnsureStatus(OrderStatus expected, string action)
    {
        if (Status != expected)
        {
            throw new DomainException($"Não é possível {action} um pedido em {Status}. Esperado: {expected}.");
        }
    }
}
