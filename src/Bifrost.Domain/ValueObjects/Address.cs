using Bifrost.Domain.Common;

namespace Bifrost.Domain.ValueObjects;

/// <summary>
/// Endereço de entrega. É copiado para o pedido no momento da compra (snapshot):
/// se o cliente mudar o endereço depois, o pedido continua mostrando para onde foi enviado.
/// </summary>
public sealed record Address
{
    private static readonly HashSet<string> States =
    [
        "AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA",
        "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO",
    ];

    private Address(string street, string number, string city, string state, string zipCode)
    {
        Street = street;
        Number = number;
        City = city;
        State = state;
        ZipCode = zipCode;
    }

    public string Street { get; }

    public string Number { get; }

    public string City { get; }

    public string State { get; }

    /// <summary>CEP com 8 dígitos, sem hífen.</summary>
    public string ZipCode { get; }

    public static Address Create(string street, string number, string city, string state, string zipCode)
    {
        var normalizedState = state?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!States.Contains(normalizedState))
        {
            throw new DomainException($"UF inválida: '{state}'.");
        }

        var digits = new string((zipCode ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (digits.Length != 8)
        {
            throw new DomainException($"CEP inválido: '{zipCode}'. Informe 8 dígitos.");
        }

        return new Address(
            Required(street, "logradouro"),
            Required(number, "número"),
            Required(city, "cidade"),
            normalizedState,
            digits);
    }

    private static string Required(string value, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new DomainException($"O campo {field} do endereço é obrigatório.");
        }

        return trimmed;
    }
}
