namespace Bifrost.Domain.Common;

/// <summary>
/// Quem acumula eventos de domínio. Sem o tipo do id, para a infraestrutura recolher os eventos de qualquer agregado.
/// </summary>
public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}
