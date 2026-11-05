using Bifrost.Application.Abstractions;
using Bifrost.Application.Orders;
using Bifrost.Domain.Catalog;
using Bifrost.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Bifrost.Infrastructure.Persistence;

internal sealed class OrderRepository(BifrostDbContext context) : IOrderRepository
{
    public Task<Order?> GetByIdAsync(OrderId id, CancellationToken cancellationToken) =>
        context.Orders.Include(o => o.Items).SingleOrDefaultAsync(o => o.Id == id, cancellationToken);

    public void Add(Order order) => context.Orders.Add(order);
}

internal sealed class ProductRepository(BifrostDbContext context) : IProductRepository
{
    public async Task<IReadOnlyList<Product>> GetByIdsAsync(
        IReadOnlyCollection<ProductId> ids,
        CancellationToken cancellationToken) =>
        await context.Products.Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);
}

internal sealed class UnitOfWork(BifrostDbContext context) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}

/// <summary>Leitura sem rastreamento: não paga o custo do change tracking para dados que não serão alterados.</summary>
internal sealed class OrderQueries(BifrostDbContext context) : IOrderQueries
{
    public async Task<OrderDto?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var id = new OrderId(orderId);
        var order = await context.Orders.AsNoTracking()
            .Include(o => o.Items)
            .SingleOrDefaultAsync(o => o.Id == id, cancellationToken);

        return order is null
            ? null
            : new OrderDto(
                order.Id.Value,
                order.CustomerId.Value,
                order.Status.ToString(),
                order.Currency,
                order.Total.Amount,
                order.Items.Select(i => new OrderItemDto(i.ProductId.Value, i.ProductName, i.UnitPrice.Amount, i.Quantity)).ToList());
    }

    public async Task<IReadOnlyList<ProductDto>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var products = await context.Products.AsNoTracking().OrderBy(p => p.Sku).ToListAsync(cancellationToken);
        return products
            .Select(p => new ProductDto(p.Id.Value, p.Sku, p.Name, p.Price.Amount, p.Price.Currency, p.StockQuantity))
            .ToList();
    }
}
