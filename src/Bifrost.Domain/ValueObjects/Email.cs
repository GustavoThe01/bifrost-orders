using System.Text.RegularExpressions;
using Bifrost.Domain.Common;

namespace Bifrost.Domain.ValueObjects;

/// <summary>
/// E-mail normalizado em minúsculas. A validação é propositalmente simples:
/// a confirmação de que o e-mail existe é feita por mensagem de verificação, não por regex.
/// </summary>
public sealed partial record Email
{
    private const int MaxLength = 254;

    private Email(string value) => Value = value;

    public string Value { get; }

    public static Email Create(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;

        if (normalized.Length is 0 or > MaxLength || !EmailPattern().IsMatch(normalized))
        {
            throw new DomainException($"E-mail inválido: '{value}'.");
        }

        return new Email(normalized);
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[a-z]{2,}$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}
