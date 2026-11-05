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
}

/// <summary>Confirma de uma só vez tudo o que os repositórios alteraram.</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Lado de leitura: devolve DTOs, sem passar pelos agregados. No Dia 06 as leituras críticas vão para o Dapper.</summary>
public interface IOrderQueries
{
    Task<OrderDto?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProductDto>> ListProductsAsync(CancellationToken cancellationToken);
}
