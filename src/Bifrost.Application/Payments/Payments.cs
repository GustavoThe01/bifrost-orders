using Bifrost.Application.Abstractions;
using Bifrost.Application.Messaging;
using Bifrost.Application.Orders;
using Bifrost.Application.Results;
using Bifrost.Domain.Orders;

namespace Bifrost.Application.Payments;

/// <summary>Cobrança de um pedido. A chave de idempotência garante que repetir o pedido não cobra duas vezes.</summary>
public sealed record PaymentRequest(Guid OrderId, decimal Amount, string Currency, string IdempotencyKey);

public enum PaymentStatus
{
    Approved,
    Declined,

    /// <summary>O gateway não respondeu a tempo, falhou nas novas tentativas ou o circuito está aberto.</summary>
    Unavailable,
}

public sealed record PaymentOutcome(PaymentStatus Status, string? TransactionId, string? Reason)
{
    public static PaymentOutcome Approved(string transactionId) => new(PaymentStatus.Approved, transactionId, null);

    public static PaymentOutcome Declined(string reason) => new(PaymentStatus.Declined, null, reason);

    public static PaymentOutcome Unavailable(string reason) => new(PaymentStatus.Unavailable, null, reason);
}

/// <summary>Porta de saída para o gateway de pagamento. A aplicação não sabe que do outro lado existe HTTP.</summary>
public interface IPaymentGateway
{
    Task<PaymentOutcome> ChargeAsync(PaymentRequest request, CancellationToken cancellationToken);
}

public sealed record PayOrderCommand(Guid OrderId) : ICommand<Result<OrderStatus>>;

/// <summary>
/// Paga um pedido confirmado. O status é conferido antes da cobrança: um pedido que não pode ser pago nunca chega ao
/// gateway. A chave de idempotência é o id do pedido, então duas tentativas de pagar o mesmo pedido viram uma
/// cobrança só no gateway.
/// </summary>
internal sealed class PayOrderHandler(IOrderRepository orders, IPaymentGateway gateway, IUnitOfWork unitOfWork)
    : ICommandHandler<PayOrderCommand, Result<OrderStatus>>
{
    public async Task<Result<OrderStatus>> HandleAsync(PayOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await orders.GetByIdAsync(new OrderId(command.OrderId), cancellationToken);
        if (order is null)
        {
            return OrderErrors.OrderNotFound(command.OrderId);
        }

        if (order.Status != OrderStatus.Confirmed)
        {
            return OrderErrors.NotPayable(order.Status);
        }

        var outcome = await gateway.ChargeAsync(
            new PaymentRequest(order.Id.Value, order.Total.Amount, order.Total.Currency, IdempotencyKey: $"order-{order.Id.Value}"),
            cancellationToken);

        switch (outcome.Status)
        {
            case PaymentStatus.Declined:
                return OrderErrors.PaymentDeclined(outcome.Reason ?? "pagamento recusado");
            case PaymentStatus.Unavailable:
                return OrderErrors.PaymentUnavailable();
        }

        order.MarkAsPaid(outcome.TransactionId!);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return order.Status;
    }
}
