using System.Collections.Concurrent;

namespace Bifrost.Api.Simulator;

/// <summary>Configuração do simulador (seção <c>PaymentSimulator</c>). Só existe em desenvolvimento.</summary>
public sealed class PaymentSimulatorOptions
{
    public const string SectionName = "PaymentSimulator";

    /// <summary>Fração das chamadas que responde 503, para ver as novas tentativas e o circuit breaker em ação.</summary>
    public double FailureRate { get; set; }

    /// <summary>Atraso de cada resposta, para ver o timeout por tentativa em ação.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    /// <summary>Acima deste valor, a cobrança é recusada, como um limite de cartão.</summary>
    public decimal DeclineAbove { get; set; } = 10_000m;
}

/// <summary>
/// Um gateway de pagamento de mentira, servido pela própria API em <c>/simulador/pagamentos</c>, só em desenvolvimento.
/// Ele segue o contrato de um gateway real: aprova (201), recusa (402) e respeita a chave de idempotência.
/// </summary>
public static class PaymentSimulator
{
    private sealed record Charge(decimal Amount, string Currency, Guid Reference);

    // Chave de idempotência → transação. A segunda chamada com a mesma chave recebe a mesma transação.
    private static readonly ConcurrentDictionary<string, string> Charges = new(StringComparer.Ordinal);

    public static IEndpointRouteBuilder MapPaymentSimulator(this IEndpointRouteBuilder app, PaymentSimulatorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        app.MapPost("/simulador/pagamentos/charges", async (HttpRequest request, Charge charge, ILoggerFactory loggers, CancellationToken token) =>
            {
                var logger = loggers.CreateLogger("Bifrost.Simulador");
                if (options.Delay > TimeSpan.Zero)
                {
                    await Task.Delay(options.Delay, token);
                }

                if (Random.Shared.NextDouble() < options.FailureRate)
                {
                    logger.LogInformation("Simulador de pagamento: falha simulada (503) para {Reference}", charge.Reference);
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                }

                if (charge.Amount > options.DeclineAbove)
                {
                    return Results.Json(new { status = "declined", reason = "limite do cartão excedido" }, statusCode: StatusCodes.Status402PaymentRequired);
                }

                var key = request.Headers["Idempotency-Key"].ToString();
                var id = Charges.GetOrAdd(key.Length > 0 ? key : Guid.NewGuid().ToString("N"), _ => "pay_" + Guid.NewGuid().ToString("N")[..12]);
                logger.LogInformation("Simulador de pagamento: {Amount} {Currency} aprovado ({TransactionId})", charge.Amount, charge.Currency, id);
                return Results.Json(new { id, status = "approved" }, statusCode: StatusCodes.Status201Created);
            })
            .AllowAnonymous()
            .ExcludeFromDescription();
        return app;
    }
}
