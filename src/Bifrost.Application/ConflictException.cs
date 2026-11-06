namespace Bifrost.Application;

/// <summary>A operação entra em conflito com o que já existe (ex.: SKU repetido). No Dia 07, passa a voltar como Result.</summary>
public sealed class ConflictException : Exception
{
    public ConflictException(string message)
        : base(message)
    {
    }

    public ConflictException()
    {
    }

    public ConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
