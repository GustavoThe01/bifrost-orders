using Bifrost.Application.Abstractions;
using Bifrost.Application.Messaging;
using Bifrost.Application.Results;
using Bifrost.Domain.Orders;
using Bifrost.Domain.ValueObjects;
using FluentValidation;

namespace Bifrost.Application.Orders;

public sealed record ShippingAddressInput(string Street, string Number, string City, string State, string ZipCode);

public sealed record OrderItemInput(Guid ProductId, int Quantity);

public sealed record CreateOrderCommand(
    Guid CustomerId,
    ShippingAddressInput ShippingAddress,
    string Currency,
    IReadOnlyList<OrderItemInput> Items) : ICommand<Result<Guid>>;

public sealed record ConfirmOrderCommand(Guid OrderId) : ICommand<Result<OrderStatus>>;

public sealed record CancelOrderCommand(Guid OrderId, string Reason) : ICommand<Result<OrderStatus>>;

public sealed class CreateOrderValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderValidator()
    {
        RuleFor(c => c.CustomerId).NotEmpty();
        RuleFor(c => c.Currency).NotEmpty().Length(3);
        RuleFor(c => c.ShippingAddress).NotNull();
        RuleFor(c => c.Items).NotEmpty();
        RuleForEach(c => c.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty();
            item.RuleFor(i => i.Quantity).InclusiveBetween(1, OrderItem.MaxQuantityPerItem);
        });
    }
}

public sealed class CancelOrderValidator : AbstractValidator<CancelOrderCommand>
{
    public CancelOrderValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(500);
    }
}

/// <summary>
/// Cria o pedido com o nome e o preço atuais do catálogo. O preço nunca vem de quem chama a API:
/// se viesse, o cliente poderia escolher quanto pagar.
/// </summary>
internal sealed class CreateOrderHandler(IProductRepository products, IOrderRepository orders, IUnitOfWork unitOfWork)
    : ICommandHandler<CreateOrderCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        var ids = command.Items.Select(i => new ProductId(i.ProductId)).Distinct().ToList();
        var catalog = (await products.GetByIdsAsync(ids, cancellationToken)).ToDictionary(p => p.Id);

        var address = command.ShippingAddress;
        var order = Order.Create(
            new CustomerId(command.CustomerId),
            Address.Create(address.Street, address.Number, address.City, address.State, address.ZipCode),
            command.Currency);

        foreach (var item in command.Items)
        {
            if (!catalog.TryGetValue(new ProductId(item.ProductId), out var product))
            {
                return OrderErrors.ProductNotFound(item.ProductId);
            }

            order.AddItem(product.Id, product.Name, product.Price, item.Quantity);
        }

        orders.Add(order);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return order.Id.Value;
    }
}

internal sealed class ConfirmOrderHandler(IOrderRepository orders, IUnitOfWork unitOfWork)
    : ICommandHandler<ConfirmOrderCommand, Result<OrderStatus>>
{
    public async Task<Result<OrderStatus>> HandleAsync(ConfirmOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await orders.GetByIdAsync(new OrderId(command.OrderId), cancellationToken);
        if (order is null)
        {
            return OrderErrors.OrderNotFound(command.OrderId);
        }

        order.Confirm();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return order.Status;
    }
}

internal sealed class CancelOrderHandler(IOrderRepository orders, IUnitOfWork unitOfWork)
    : ICommandHandler<CancelOrderCommand, Result<OrderStatus>>
{
    public async Task<Result<OrderStatus>> HandleAsync(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await orders.GetByIdAsync(new OrderId(command.OrderId), cancellationToken);
        if (order is null)
        {
            return OrderErrors.OrderNotFound(command.OrderId);
        }

        order.Cancel(command.Reason);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return order.Status;
    }
}

/// <summary>Falhas esperadas dos casos de uso de pedido, com códigos estáveis para quem consome a API.</summary>
public static class OrderErrors
{
    public static Error OrderNotFound(Guid id) => Error.NotFound("order.not_found", $"Pedido {id} não encontrado.");

    public static Error ProductNotFound(Guid id) => Error.NotFound("product.not_found", $"Produto {id} não encontrado.");
}
