namespace Bifrost.Domain.Orders;

/// <summary>
/// Estados do pedido: Pending → Confirmed → Paid → Shipped → Delivered.
/// Cancelled só é possível antes do pagamento.
/// </summary>
public enum OrderStatus
{
    /// <summary>Criado; os itens ainda podem ser alterados.</summary>
    Pending = 1,

    /// <summary>Confirmado pelo cliente; o estoque fica reservado (Dia 09).</summary>
    Confirmed = 2,

    /// <summary>Pagamento aprovado.</summary>
    Paid = 3,

    /// <summary>Entregue à transportadora.</summary>
    Shipped = 4,

    /// <summary>Recebido pelo cliente. Estado final.</summary>
    Delivered = 5,

    /// <summary>Cancelado antes do pagamento. Estado final; o estoque reservado volta.</summary>
    Cancelled = 6,
}
