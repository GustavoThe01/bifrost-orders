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

    public Task<bool> SkuExistsAsync(string sku, CancellationToken cancellationToken) =>
        context.Products.AnyAsync(p => p.Sku == sku, cancellationToken);

    public void Add(Product product) => context.Products.Add(product);
}

internal sealed class UnitOfWork(BifrostDbContext context) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
