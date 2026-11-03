using Bifrost.Domain.Common;
using Bifrost.Domain.ValueObjects;

namespace Bifrost.Domain.Orders;

/// <summary>
/// Item do pedido. O nome e o preço do produto são copiados no momento da compra:
/// reajustar o catálogo depois não muda pedidos já feitos.
/// </summary>
public sealed class OrderItem
{
    public const int MaxQuantityPerItem = 99;

    internal OrderItem(ProductId productId, string productName, Money unitPrice, int quantity)
    {
        if (string.IsNullOrWhiteSpace(productName))
        {
            throw new DomainException("O nome do produto é obrigatório.");
        }

        if (unitPrice.IsZero)
        {
            throw new DomainException("O preço unitário deve ser maior que zero.");
        }

        ProductId = productId;
        ProductName = productName.Trim();
        UnitPrice = unitPrice;
        Quantity = ValidateQuantity(quantity);
    }

    public ProductId ProductId { get; }

    public string ProductName { get; }

    public Money UnitPrice { get; }

    public int Quantity { get; private set; }

    public Money Total => UnitPrice * Quantity;

    internal void IncreaseQuantity(int quantity) => Quantity = ValidateQuantity(Quantity + quantity);

    private static int ValidateQuantity(int quantity)
    {
        if (quantity is < 1 or > MaxQuantityPerItem)
        {
            throw new DomainException($"A quantidade de cada item deve estar entre 1 e {MaxQuantityPerItem}: {quantity}.");
        }

        return quantity;
    }
}
