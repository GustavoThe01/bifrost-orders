using Bifrost.Domain.Catalog;
using Bifrost.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bifrost.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products", table =>
            table.HasCheckConstraint("ck_products_stock", "stock_quantity >= 0"));

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new ProductId(value))
            .ValueGeneratedNever();

        builder.Property(p => p.Sku).HasColumnName("sku").HasMaxLength(20).IsRequired();
        builder.HasIndex(p => p.Sku).IsUnique();

        builder.Property(p => p.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        builder.Property(p => p.StockQuantity).HasColumnName("stock_quantity");

        // Token de concorrência: todo UPDATE do produto inclui "WHERE version = <versão lida>".
        builder.Property(p => p.Version).HasColumnName("version").IsConcurrencyToken();

        builder.ComplexProperty(p => p.Price).MapMoney("price_cents", "currency");

        builder.Ignore(p => p.DomainEvents);
    }
}
