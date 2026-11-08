using Bifrost.Api.Errors;
using Bifrost.Application.Catalog;
using Bifrost.Application.Messaging;
using Bifrost.Application.Orders;
using Microsoft.AspNetCore.Mvc;

namespace Bifrost.Api.Controllers;

[ApiController]
[Route("api/v1/products")]
public sealed class ProductsController(IDispatcher dispatcher) : ControllerBase
{
    public sealed record CreateProductRequest(string Sku, string Name, decimal Price, string Currency, int StockQuantity);

    public sealed record CreatedResponse(Guid Id);

    /// <summary>Lista o catálogo.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ProductDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ProductDto>>> List(CancellationToken cancellationToken) =>
        this.ToActionResult(await dispatcher.QueryAsync(new ListProductsQuery(), cancellationToken));

    /// <summary>Cadastra um produto. No Dia 11, passa a exigir o perfil de administrador.</summary>
    [HttpPost]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<CreatedResponse>> Create(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(
            new CreateProductCommand(request.Sku, request.Name, request.Price, request.Currency, request.StockQuantity),
            cancellationToken);

        return this.ToActionResult(result, id => CreatedAtAction(nameof(List), null, new CreatedResponse(id)));
    }
}
