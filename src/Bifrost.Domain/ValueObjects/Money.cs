using System.Globalization;
using Bifrost.Domain.Common;

namespace Bifrost.Domain.ValueObjects;

/// <summary>
/// Valor monetário com moeda (ISO 4217). Em pedidos nunca é negativo.
/// </summary>
public readonly record struct Money
{
    private const int DecimalPlaces = 2;

    public decimal Amount { get; }

    public string Currency { get; }

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Money Create(decimal amount, string currency)
    {
        if (amount < 0m)
        {
            throw new DomainException($"O valor não pode ser negativo: {amount}.");
        }

        if (decimal.Round(amount, DecimalPlaces) != amount)
        {
            throw new DomainException($"O valor deve ter no máximo {DecimalPlaces} casas decimais: {amount}.");
        }

        return new Money(amount, NormalizeCurrency(currency));
    }

    public static Money Zero(string currency) => new(0m, NormalizeCurrency(currency));

    public bool IsZero => Amount == 0m;

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator *(Money value, int quantity)
    {
        if (quantity < 0)
        {
            throw new DomainException($"A quantidade não pode ser negativa: {quantity}.");
        }

        return new Money(value.Amount * quantity, value.Currency);
    }

    public Money Add(Money other) => this + other;

    public Money Multiply(int quantity) => this * quantity;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Currency} {Amount:0.00}");

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (left.Currency != right.Currency)
        {
            throw new DomainException($"Não é possível operar {left.Currency} com {right.Currency}.");
        }
    }

    private static string NormalizeCurrency(string currency)
    {
        var normalized = currency?.Trim().ToUpperInvariant() ?? string.Empty;

        if (normalized.Length != 3 || !normalized.All(char.IsAsciiLetterUpper))
        {
            throw new DomainException($"Moeda inválida: '{currency}'. Use o código ISO de 3 letras (ex.: BRL).");
        }

        return normalized;
    }
}
