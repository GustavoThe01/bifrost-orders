namespace Bifrost.Domain.Common;

/// <summary>
/// Violação de uma invariante do domínio (valor inválido ou operação proibida no estado atual).
/// </summary>
/// <remarks>
/// O domínio continua lançando exceção quando uma invariante seria quebrada. Na fronteira da aplicação,
/// o pipeline converte essa exceção em <c>Result</c>, e a API nunca vê a exceção (ADR 0004).
/// </remarks>
public sealed class DomainException : Exception
{
    public DomainException(string message)
        : base(message)
    {
    }

    public DomainException()
    {
    }

    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
