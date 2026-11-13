using System.Data.Common;
using Bifrost.Application.Abstractions;
using Bifrost.Application.Orders;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace Bifrost.Infrastructure.Persistence;

/// <summary>
/// Lado de leitura. Pedidos são lidos com Dapper e SQL escrito à mão; o catálogo continua no EF Core,
/// com projeção direta para DTO. A decisão está no ADR 0003.
/// </summary>
/// <remarks>
/// O Dapper usa a mesma conexão do <see cref="BifrostDbContext"/>, então lê exatamente o banco que o EF Core escreve,
/// sem uma segunda connection string. O EF Core guarda Guid como TEXT em maiúsculas, por isso os parâmetros
/// de id passam por <see cref="Key"/>.
/// </remarks>
internal sealed class OrderQueries(BifrostDbContext context) : IOrderQueries
{
    private const string OrderByIdSql = """
        SELECT id AS Id, customer_id AS CustomerId, status AS Status, currency AS Currency
        FROM orders
        WHERE id = @Id;

        SELECT product_id AS ProductId, product_name AS ProductName,
               unit_price_cents AS UnitPriceCents, quantity AS Quantity
        FROM order_items
        WHERE order_id = @Id
        ORDER BY product_name;
        """;

    // O total sai do banco já somado em centavos: uma consulta só, sem carregar os itens.
    // Ids são UUID v7, que crescem com o tempo; ordenar pelo id é ordenar pela criação.
    private const string ListOrdersSql = """
        SELECT o.id AS Id, o.customer_id AS CustomerId, o.status AS Status, o.currency AS Currency,
               COALESCE(SUM(i.unit_price_cents * i.quantity), 0) AS TotalCents,
               COUNT(i.product_id) AS ItemCount
        FROM orders o
        LEFT JOIN order_items i ON i.order_id = o.id
        WHERE (@CustomerId IS NULL OR o.customer_id = @CustomerId)
          AND (@Status IS NULL OR o.status = @Status)
        GROUP BY o.id, o.customer_id, o.status, o.currency
        ORDER BY o.id DESC
        LIMIT @PageSize OFFSET @Offset;

        SELECT COUNT(*)
        FROM orders o
        WHERE (@CustomerId IS NULL OR o.customer_id = @CustomerId)
          AND (@Status IS NULL OR o.status = @Status);
        """;

    private DbConnection Connection => context.Database.GetDbConnection();

    public async Task<OrderDto?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken)
    {
        await using var results = await Connection.QueryMultipleAsync(
            new CommandDefinition(OrderByIdSql, new { Id = Key(orderId) }, cancellationToken: cancellationToken));

        var order = await results.ReadSingleOrDefaultAsync<OrderRow>();
        if (order is null)
        {
            return null;
        }

        var items = (await results.ReadAsync<ItemRow>())
            .Select(i => new OrderItemDto(Guid.Parse(i.ProductId), i.ProductName, Money(i.UnitPriceCents), (int)i.Quantity))
            .ToList();

        return new OrderDto(
            Guid.Parse(order.Id),
            Guid.Parse(order.CustomerId),
            order.Status,
            order.Currency,
            items.Sum(i => i.UnitPrice * i.Quantity),
            items);
    }

    public async Task<PagedResult<OrderSummaryDto>> ListOrdersAsync(
        OrderListFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var parameters = new
        {
            CustomerId = filter.CustomerId is { } customer ? Key(customer) : null,
            Status = filter.Status?.ToString(),
            PageSize = pageSize,
            Offset = (page - 1) * pageSize,
        };

        await using var results = await Connection.QueryMultipleAsync(
            new CommandDefinition(ListOrdersSql, parameters, cancellationToken: cancellationToken));

        var items = (await results.ReadAsync<SummaryRow>())
            .Select(o => new OrderSummaryDto(
                Guid.Parse(o.Id),
                Guid.Parse(o.CustomerId),
                o.Status,
                o.Currency,
                Money(o.TotalCents),
                (int)o.ItemCount))
            .ToList();
        var total = await results.ReadSingleAsync<long>();

        return new PagedResult<OrderSummaryDto>(items, page, pageSize, (int)total);
    }

    /// <summary>
    /// Catálogo pelo EF Core: sem rastreamento e com projeção no próprio Select. O SQL busca só as colunas usadas.
    /// </summary>
    public async Task<IReadOnlyList<ProductDto>> ListProductsAsync(CancellationToken cancellationToken) =>
        await context.Products
            .AsNoTracking()
            .OrderBy(p => p.Sku)
            .Select(p => new ProductDto(p.Id.Value, p.Sku, p.Name, p.Price.Amount, p.Price.Currency, p.StockQuantity))
            .ToListAsync(cancellationToken);

    private static string Key(Guid id) => id.ToString().ToUpperInvariant();

    private static decimal Money(long cents) => cents / 100m;

    // Linhas cruas do banco. O SQLite devolve TEXT e INTEGER (long); a conversão para o DTO fica explícita acima.
    private sealed record OrderRow(string Id, string CustomerId, string Status, string Currency);

    private sealed record ItemRow(string ProductId, string ProductName, long UnitPriceCents, long Quantity);

    /// <remarks>
    /// Classe com propriedades, e não record posicional. TotalCents e ItemCount são agregações (SUM, COUNT), e o SQLite
    /// só sabe o tipo delas quando há linhas: num resultado vazio, ele as declara como BLOB. O Dapper monta o construtor
    /// de um record a partir desses tipos e falhava com uma página vazia (um cliente sem pedidos dava 500).
    /// Com propriedades, a conversão é feita linha a linha, e uma página vazia não tem nada a converter.
    /// </remarks>
    private sealed class SummaryRow
    {
        public string Id { get; init; } = string.Empty;

        public string CustomerId { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public string Currency { get; init; } = string.Empty;

        public long TotalCents { get; init; }

        public long ItemCount { get; init; }
    }
}
