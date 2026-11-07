using Bifrost.Application.Abstractions;
using Bifrost.Application.Messaging;
using Bifrost.Domain.Orders;
using FluentValidation;

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

/// <summary>Linha da listagem: o resumo do pedido, sem os itens.</summary>
public sealed record OrderSummaryDto(Guid Id, Guid CustomerId, string Status, string Currency, decimal Total, int ItemCount);

/// <summary>Uma página de resultados. A página começa em 1.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalItems)
{
    public int TotalPages => TotalItems == 0 ? 0 : (int)Math.Ceiling(TotalItems / (double)PageSize);

    public bool HasNextPage => Page < TotalPages;
}

public sealed record GetOrderByIdQuery(Guid OrderId) : IQuery<OrderDto>;

/// <summary>Lista pedidos, do mais recente para o mais antigo. Cliente e status são filtros opcionais.</summary>
public sealed record ListOrdersQuery(Guid? CustomerId = null, string? Status = null, int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<OrderSummaryDto>>
{
    public const int MaxPageSize = 100;
}

public sealed record ListProductsQuery : IQuery<IReadOnlyList<ProductDto>>;

internal sealed class GetOrderByIdHandler(IOrderQueries queries) : IQueryHandler<GetOrderByIdQuery, OrderDto>
{
    public async Task<OrderDto> HandleAsync(GetOrderByIdQuery query, CancellationToken cancellationToken) =>
        await queries.GetByIdAsync(query.OrderId, cancellationToken)
        ?? throw new NotFoundException($"Pedido {query.OrderId} não encontrado.");
}

internal sealed class ListOrdersValidator : AbstractValidator<ListOrdersQuery>
{
    public ListOrdersValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, ListOrdersQuery.MaxPageSize);
        RuleFor(q => q.CustomerId).NotEqual(Guid.Empty);
        RuleFor(q => q.Status)
            .Must(status => Enum.GetNames<OrderStatus>().Contains(status, StringComparer.OrdinalIgnoreCase))
            .When(q => q.Status is not null)
            .WithMessage("Status desconhecido. Use Pending, Confirmed, Paid, Shipped, Delivered ou Cancelled.");
    }
}

internal sealed class ListOrdersHandler(IOrderQueries queries)
    : IQueryHandler<ListOrdersQuery, PagedResult<OrderSummaryDto>>
{
    public Task<PagedResult<OrderSummaryDto>> HandleAsync(ListOrdersQuery query, CancellationToken cancellationToken)
    {
        OrderStatus? status = query.Status is null ? null : Enum.Parse<OrderStatus>(query.Status, ignoreCase: true);
        return queries.ListOrdersAsync(new OrderListFilter(query.CustomerId, status), query.Page, query.PageSize, cancellationToken);
    }
}

internal sealed class ListProductsHandler(IOrderQueries queries) : IQueryHandler<ListProductsQuery, IReadOnlyList<ProductDto>>
{
    public Task<IReadOnlyList<ProductDto>> HandleAsync(ListProductsQuery query, CancellationToken cancellationToken) =>
        queries.ListProductsAsync(cancellationToken);
}
