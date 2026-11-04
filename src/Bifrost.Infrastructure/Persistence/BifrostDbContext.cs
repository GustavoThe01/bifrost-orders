using Bifrost.Domain.Catalog;
using Bifrost.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Bifrost.Infrastructure.Persistence;

public sealed class BifrostDbContext(DbContextOptions<BifrostDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BifrostDbContext).Assembly);
    }
}
