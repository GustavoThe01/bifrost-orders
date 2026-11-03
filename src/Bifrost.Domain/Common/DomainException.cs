namespace Bifrost.Domain.Common;

/// <summary>
/// Violação de uma invariante do domínio (valor inválido ou operação proibida no estado atual).
/// </summary>
/// <remarks>
/// No Dia 07, as falhas de negócio esperadas passam a voltar como <c>Result</c> e viram ProblemDetails na API.
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
