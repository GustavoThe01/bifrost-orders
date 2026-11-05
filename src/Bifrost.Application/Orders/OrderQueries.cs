using Bifrost.Application.Abstractions;
using Bifrost.Application.Messaging;

namespace Bifrost.Application.Orders;

public sealed record ProductDto(Guid Id, string Sku, string Name, decimal Price, string Currency, int StockQuantity);

public sealed record OrderItemDto(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity);

public sealed record OrderDto(
    Guid Id,
    Guid CustomerId,
    string Status,
    string Currency,
    decimal Total,
    IReadOnlyList<OrderItemDto> Items);

public sealed record GetOrderByIdQuery(Guid OrderId) : IQuery<OrderDto>;

public sealed record ListProductsQuery : IQuery<IReadOnlyList<ProductDto>>;

internal sealed class GetOrderByIdHandler(IOrderQueries queries) : IQueryHandler<GetOrderByIdQuery, OrderDto>
{
    public async Task<OrderDto> HandleAsync(GetOrderByIdQuery query, CancellationToken cancellationToken) =>
        await queries.GetByIdAsync(query.OrderId, cancellationToken)
        ?? throw new NotFoundException($"Pedido {query.OrderId} não encontrado.");
}

internal sealed class ListProductsHandler(IOrderQueries queries) : IQueryHandler<ListProductsQuery, IReadOnlyList<ProductDto>>
{
    public Task<IReadOnlyList<ProductDto>> HandleAsync(ListProductsQuery query, CancellationToken cancellationToken) =>
        queries.ListProductsAsync(cancellationToken);
}
