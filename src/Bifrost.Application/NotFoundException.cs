namespace Bifrost.Application;

/// <summary>O recurso pedido não existe. No Dia 07, esse caso passa a voltar como Result.</summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string message)
        : base(message)
    {
    }

    public NotFoundException()
    {
    }

    public NotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
