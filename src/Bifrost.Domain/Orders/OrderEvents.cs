using Bifrost.Domain.Common;
using Bifrost.Domain.ValueObjects;

namespace Bifrost.Domain.Orders;

public sealed record OrderCreated(OrderId OrderId, CustomerId CustomerId) : IDomainEvent;

public sealed record OrderConfirmed(OrderId OrderId, Money Total) : IDomainEvent;

public sealed record OrderPaid(OrderId OrderId) : IDomainEvent;

public sealed record OrderShipped(OrderId OrderId) : IDomainEvent;

public sealed record OrderDelivered(OrderId OrderId) : IDomainEvent;

public sealed record OrderCancelled(OrderId OrderId, OrderStatus PreviousStatus, string Reason) : IDomainEvent;
