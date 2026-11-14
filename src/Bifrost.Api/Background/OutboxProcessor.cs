using Bifrost.Infrastructure.Outbox;
using Microsoft.Extensions.Options;

namespace Bifrost.Api.Background;

/// <summary>
/// Roda em segundo plano: a cada intervalo, publica as mensagens pendentes do Outbox.
/// </summary>
/// <remarks>
/// Cada rodada usa um escopo novo de DI, com o próprio DbContext, como uma requisição. Uma falha numa rodada é
/// registrada e não derruba o serviço: a rodada seguinte tenta de novo. Com uma instância só da API, não há disputa
/// pelas mensagens; com várias, seria preciso travar o lote (SELECT ... FOR UPDATE SKIP LOCKED no PostgreSQL).
/// </remarks>
public sealed partial class OutboxProcessor(
    IServiceScopeFactory scopes,
    IOptions<OutboxOptions> options,
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(settings.PollingInterval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var published = await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().ProcessBatchAsync(settings, stoppingToken);
                if (published > 0)
                {
                    LogBatch(logger, published);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogRoundFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox: {Count} mensagem(ns) publicada(s)")]
    private static partial void LogBatch(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox: a rodada falhou e será repetida no próximo intervalo")]
    private static partial void LogRoundFailed(ILogger logger, Exception exception);
}
