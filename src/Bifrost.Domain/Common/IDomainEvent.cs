namespace Bifrost.Domain.Common;

/// <summary>
/// Algo relevante que aconteceu no domínio. No Dia 13 os eventos passam a ser gravados no Outbox,
/// na mesma transação do agregado, e é lá que recebem a data e hora de ocorrência.
/// </summary>
public interface IDomainEvent;
