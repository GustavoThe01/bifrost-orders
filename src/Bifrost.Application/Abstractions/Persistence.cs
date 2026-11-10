using Bifrost.Application.Orders;
using Bifrost.Domain.Catalog;
using Bifrost.Domain.Orders;

namespace Bifrost.Application.Abstractions;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(OrderId id, CancellationToken cancellationToken);

    void Add(Order order);
}

public interface IProductRepository
{
    Task<IReadOnlyList<Product>> GetByIdsAsync(IReadOnlyCollection<ProductId> ids, CancellationToken cancellationToken);

    Task<bool> SkuExistsAsync(string sku, CancellationToken cancellationToken);

    void Add(Product product);
}

/// <summary>
/// Outra operação alterou os mesmos dados entre a leitura e a gravação. Nada foi gravado.
/// A infraestrutura traduz o erro do EF Core para esta exceção, e a aplicação não precisa conhecer o EF.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ConcurrencyConflictException(string message)
        : base(message)
    {
    }

    public ConcurrencyConflictException()
    {
    }
}

/// <summary>Confirma de uma só vez tudo o que os repositórios alteraram.</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Filtros da listagem de pedidos. Campo nulo significa "sem filtro".</summary>
public sealed record OrderListFilter(Guid? CustomerId, OrderStatus? Status);

/// <summary>Lado de leitura: devolve DTOs, sem passar pelos agregados nem pelo change tracking.</summary>
public interface IOrderQueries
{
    Task<OrderDto?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken);

    Task<PagedResult<OrderSummaryDto>> ListOrdersAsync(
        OrderListFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ProductDto>> ListProductsAsync(CancellationToken cancellationToken);
}
