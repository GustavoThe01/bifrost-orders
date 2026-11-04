using Bifrost.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bifrost.Infrastructure.Persistence.Configurations;

internal static class MoneyMapping
{
    /// <summary>
    /// Mapeia <see cref="Money"/> em duas colunas: valor em centavos e moeda.
    /// </summary>
    /// <remarks>
    /// O SQLite não tem tipo decimal, e o EF Core guardaria o valor como texto, sem permitir somar
    /// nem ordenar no banco. Centavos em INTEGER são exatos e seguem a decisão do Mercurium.
    /// </remarks>
    public static void MapMoney(this ComplexPropertyBuilder<Money> money, string amountColumn, string currencyColumn)
    {
        money.Property(m => m.Amount)
            .HasColumnName(amountColumn)
            .HasConversion(amount => (long)(amount * 100m), cents => cents / 100m)
            .IsRequired();

        money.Property(m => m.Currency)
            .HasColumnName(currencyColumn)
            .HasMaxLength(3)
            .IsRequired();
    }
}
