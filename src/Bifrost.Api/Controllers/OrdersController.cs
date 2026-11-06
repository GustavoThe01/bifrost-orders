using Bifrost.Application.Messaging;
using Bifrost.Application.Orders;
using Bifrost.Domain.Orders;
using Microsoft.AspNetCore.Mvc;

namespace Bifrost.Api.Controllers;

[ApiController]
[Route("api/v1/orders")]
public sealed class OrdersController(IDispatcher dispatcher) : ControllerBase
{
    public sealed record AddressRequest(string Street, string Number, string City, string State, string ZipCode);

    public sealed record OrderItemRequest(Guid ProductId, int Quantity);

    /// <summary>O cliente vem no corpo apenas até o Dia 11, quando passa a vir do token JWT.</summary>
    public sealed record CreateOrderRequest(
        Guid CustomerId,
        AddressRequest ShippingAddress,
        string Currency,
        IReadOnlyList<OrderItemRequest> Items);

    public sealed record CancelOrderRequest(string Reason);

    public sealed record CreatedResponse(Guid Id);

    public sealed record OrderStatusResponse(Guid Id, string Status);

    /// <summary>Cria um pedido com os preços atuais do catálogo.</summary>
    [HttpPost]
    public async Task<ActionResult<CreatedResponse>> Create(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var address = request.ShippingAddress;
        var id = await dispatcher.SendAsync(
            new CreateOrderCommand(
                request.CustomerId,
                new ShippingAddressInput(address.Street, address.Number, address.City, address.State, address.ZipCode),
                request.Currency,
                request.Items.Select(item => new OrderItemInput(item.ProductId, item.Quantity)).ToList()),
            cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id }, new CreatedResponse(id));
    }

    /// <summary>Detalha um pedido.</summary>
    [HttpGet("{id:guid}")]
    public Task<OrderDto> GetById(Guid id, CancellationToken cancellationToken) =>
        dispatcher.QueryAsync(new GetOrderByIdQuery(id), cancellationToken);

    /// <summary>Confirma o pedido. A reserva de estoque entra no Dia 09.</summary>
    [HttpPost("{id:guid}/confirm")]
    public async Task<OrderStatusResponse> Confirm(Guid id, CancellationToken cancellationToken) =>
        ToResponse(id, await dispatcher.SendAsync(new ConfirmOrderCommand(id), cancellationToken));

    /// <summary>Cancela um pedido que ainda não foi pago.</summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<OrderStatusResponse> Cancel(Guid id, CancelOrderRequest request, CancellationToken cancellationToken) =>
        ToResponse(id, await dispatcher.SendAsync(new CancelOrderCommand(id, request.Reason), cancellationToken));

    private static OrderStatusResponse ToResponse(Guid id, OrderStatus status) => new(id, status.ToString());
}
