using Bifrost.Api.Errors;
using Bifrost.Application.Messaging;
using Bifrost.Application.Orders;
using Bifrost.Domain.Orders;
using Bifrost.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bifrost.Api.Controllers;

/// <remarks>Todas as rotas exigem token. A regra "cliente só vê os próprios pedidos" entra no Dia 12.</remarks>
[ApiController]
[Route("api/v1/orders")]
[Authorize]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
public sealed class OrdersController(IDispatcher dispatcher) : ControllerBase
{
    public sealed record AddressRequest(string Street, string Number, string City, string State, string ZipCode);

    public sealed record OrderItemRequest(Guid ProductId, int Quantity);

    /// <summary>O cliente não vem no corpo: é o dono do token (claim <c>sub</c>).</summary>
    public sealed record CreateOrderRequest(
        AddressRequest ShippingAddress,
        string Currency,
        IReadOnlyList<OrderItemRequest> Items);

    public sealed record CancelOrderRequest(string Reason);

    public sealed record CreatedResponse(Guid Id);

    public sealed record OrderStatusResponse(Guid Id, string Status);

    /// <summary>Cria um pedido com os preços atuais do catálogo, para o cliente dono do token.</summary>
    [HttpPost]
    [Authorize(Roles = Roles.Customer)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<CreatedResponse>> Create(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var address = request.ShippingAddress;
        var result = await dispatcher.SendAsync(
            new CreateOrderCommand(
                CustomerId(),
                new ShippingAddressInput(address.Street, address.Number, address.City, address.State, address.ZipCode),
                request.Currency,
                request.Items.Select(item => new OrderItemInput(item.ProductId, item.Quantity)).ToList()),
            cancellationToken);

        return this.ToActionResult(result, id => CreatedAtAction(nameof(GetById), new { id }, new CreatedResponse(id)));
    }

    /// <summary>Lista pedidos do mais recente para o mais antigo, com filtro por cliente e status.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<OrderSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<PagedResult<OrderSummaryDto>>> List(
        [FromQuery] Guid? customerId,
        [FromQuery] string? status,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20) =>
        this.ToActionResult(await dispatcher.QueryAsync(new ListOrdersQuery(customerId, status, page, pageSize), cancellationToken));

    /// <summary>Detalha um pedido.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<OrderDto>> GetById(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await dispatcher.QueryAsync(new GetOrderByIdQuery(id), cancellationToken));

    /// <summary>Confirma o pedido. A reserva de estoque entra no Dia 09.</summary>
    [HttpPost("{id:guid}/confirm")]
    [ProducesResponseType<OrderStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<OrderStatusResponse>> Confirm(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await dispatcher.SendAsync(new ConfirmOrderCommand(id), cancellationToken), status => Ok(ToResponse(id, status)));

    /// <summary>Cancela um pedido que ainda não foi pago.</summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType<OrderStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<OrderStatusResponse>> Cancel(Guid id, CancelOrderRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(
            await dispatcher.SendAsync(new CancelOrderCommand(id, request.Reason), cancellationToken),
            status => Ok(ToResponse(id, status)));

    /// <summary>O id do cliente que está no token. A rota só aceita o perfil Customer, que sempre tem esse claim.</summary>
    private Guid CustomerId() => Guid.Parse(User.FindFirst("sub")!.Value);

    private static OrderStatusResponse ToResponse(Guid id, OrderStatus status) => new(id, status.ToString());
}
