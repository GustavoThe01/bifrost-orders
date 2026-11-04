using Bifrost.Domain.Catalog;
using Bifrost.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bifrost.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    private const string OrderIdColumn = "OrderId";

    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new OrderId(value))
            .ValueGeneratedNever();

        builder.Property(o => o.CustomerId)
            .HasColumnName("customer_id")
            .HasConversion(id => id.Value, value => new CustomerId(value));

        builder.Property(o => o.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();

        builder.Property(o => o.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(o => o.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(500);

        // Owned type: o endereço não tem identidade própria e fica nas colunas do próprio pedido.
        builder.OwnsOne(o => o.ShippingAddress, address =>
        {
            address.Property(a => a.Street).HasColumnName("shipping_street").HasMaxLength(200).IsRequired();
            address.Property(a => a.Number).HasColumnName("shipping_number").HasMaxLength(20).IsRequired();
            address.Property(a => a.City).HasColumnName("shipping_city").HasMaxLength(100).IsRequired();
            address.Property(a => a.State).HasColumnName("shipping_state").HasMaxLength(2).IsRequired();
            address.Property(a => a.ZipCode).HasColumnName("shipping_zip_code").HasMaxLength(8).IsRequired();
        });
        builder.Navigation(o => o.ShippingAddress).IsRequired();

        builder.HasMany(o => o.Items).WithOne().HasForeignKey(OrderIdColumn).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        // O total é calculado a partir dos itens, e os eventos vão para o Outbox no Dia 13.
        builder.Ignore(o => o.Total);
        builder.Ignore(o => o.DomainEvents);
    }
}

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    private const string OrderIdColumn = "OrderId";

    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items", table =>
            table.HasCheckConstraint("ck_order_items_quantity", "quantity BETWEEN 1 AND 99"));

        builder.Property<OrderId>(OrderIdColumn)
            .HasColumnName("order_id")
            .HasConversion(id => id.Value, value => new OrderId(value));

        builder.Property(i => i.ProductId)
            .HasColumnName("product_id")
            .HasConversion(id => id.Value, value => new ProductId(value))
            .ValueGeneratedNever();

        builder.HasKey(OrderIdColumn, nameof(OrderItem.ProductId));

        // Produto vendido não pode ser excluído: o histórico do pedido aponta para ele.
        builder.HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(i => i.ProductName).HasColumnName("product_name").HasMaxLength(120).IsRequired();
        builder.Property(i => i.Quantity).HasColumnName("quantity");

        builder.ComplexProperty(i => i.UnitPrice).MapMoney("unit_price_cents", "currency");

        builder.Ignore(i => i.Total);
    }
}
