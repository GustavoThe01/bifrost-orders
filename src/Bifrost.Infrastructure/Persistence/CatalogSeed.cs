using Bifrost.Domain.Catalog;
using Bifrost.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Bifrost.Infrastructure.Persistence;

/// <summary>Produtos iniciais do catálogo, para a API já ter o que vender em desenvolvimento.</summary>
public static class CatalogSeed
{
    public static void Seed(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var products = context.Set<Product>();
        if (products.Any())
        {
            return;
        }

        products.AddRange(
            Product.Create("CAN-001", "Caneca de cerâmica", Brl(39.90m), 120),
            Product.Create("CAM-001", "Camiseta de algodão", Brl(79.90m), 80),
            Product.Create("MOC-001", "Mochila para notebook", Brl(249.00m), 25),
            Product.Create("FON-001", "Fone de ouvido sem fio", Brl(349.90m), 40),
            Product.Create("LUM-001", "Luminária de mesa", Brl(129.50m), 15));

        context.SaveChanges();
    }

    private static Money Brl(decimal amount) => Money.Create(amount, "BRL");
}
