namespace Bifrost.Domain.Common;

/// <summary>
/// Raiz de agregado: a única porta de entrada para alterar o agregado e quem acumula os eventos de domínio.
/// </summary>
public abstract class AggregateRoot<TId> : IHasDomainEvents
    where TId : struct
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(TId id) => Id = id;

    public TId Id { get; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
}
