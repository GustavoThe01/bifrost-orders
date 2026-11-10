using Bifrost.Domain.Common;
using Bifrost.Domain.Orders;
using Bifrost.Domain.ValueObjects;

namespace Bifrost.Domain.Catalog;

/// <summary>
/// Produto do catálogo. O pedido não depende desta classe: ele guarda só o <see cref="ProductId"/>
/// e uma cópia do nome e do preço, porque cada agregado é sua própria fronteira de consistência.
/// </summary>
public sealed class Product : AggregateRoot<ProductId>
{
    private const int MaxNameLength = 120;

    // Exigido pelo EF Core para materializar a entidade.
    private Product()
        : base(default)
    {
        Sku = null!;
        Name = null!;
    }

    private Product(ProductId id, string sku, string name, Money price, int stockQuantity)
        : base(id)
    {
        Sku = sku;
        Name = name;
        Price = price;
        StockQuantity = stockQuantity;
    }

    public string Sku { get; }

    public string Name { get; private set; }

    public Money Price { get; private set; }

    /// <summary>Saldo disponível para venda. Diminui quando um pedido é confirmado e volta se ele for cancelado.</summary>
    public int StockQuantity { get; private set; }

    /// <summary>
    /// Versão do produto, usada como token de concorrência otimista. Muda a cada alteração: se duas operações leram a
    /// mesma versão, só a primeira grava; a segunda recebe um conflito em vez de sobrescrever o saldo.
    /// </summary>
    public int Version { get; private set; }

    public static Product Create(string sku, string name, Money price, int stockQuantity)
    {
        var normalizedSku = sku?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalizedSku.Length is < 3 or > 20)
        {
            throw new DomainException($"O SKU deve ter entre 3 e 20 caracteres: '{sku}'.");
        }

        if (stockQuantity < 0)
        {
            throw new DomainException($"O estoque não pode ser negativo: {stockQuantity}.");
        }

        return new Product(ProductId.New(), normalizedSku, ValidateName(name), ValidatePrice(price), stockQuantity);
    }

    public void ChangePrice(Money newPrice)
    {
        Price = ValidatePrice(newPrice);
        Version++;
    }

    /// <summary>Separa unidades para um pedido confirmado. Sem saldo, nada muda.</summary>
    public void Reserve(int quantity)
    {
        EnsurePositive(quantity);
        if (quantity > StockQuantity)
        {
            throw new DomainException($"Estoque insuficiente para {Sku}: disponível {StockQuantity}, pedido {quantity}.");
        }

        StockQuantity -= quantity;
        Version++;
    }

    /// <summary>Devolve ao saldo as unidades de um pedido cancelado.</summary>
    public void Release(int quantity)
    {
        EnsurePositive(quantity);
        StockQuantity += quantity;
        Version++;
    }

    private static void EnsurePositive(int quantity)
    {
        if (quantity <= 0)
        {
            throw new DomainException($"A quantidade deve ser maior que zero: {quantity}.");
        }
    }

    private static string ValidateName(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > MaxNameLength)
        {
            throw new DomainException($"O nome do produto deve ter entre 1 e {MaxNameLength} caracteres.");
        }

        return trimmed;
    }

    private static Money ValidatePrice(Money price)
    {
        if (price.IsZero)
        {
            throw new DomainException("O preço do produto deve ser maior que zero.");
        }

        return price;
    }
}
