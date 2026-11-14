using System.Net;
using System.Net.Http.Json;
using Bifrost.Application.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Bifrost.Infrastructure.Payments;

/// <summary>Configuração do gateway (seção <c>Payments</c>).</summary>
public sealed class PaymentOptions
{
    public const string SectionName = "Payments";

    /// <summary>Endereço base do gateway, terminado em barra.</summary>
    public Uri BaseUrl { get; set; } = new("http://localhost:5080/simulador/pagamentos/");

    /// <summary>Novas tentativas depois da primeira, com espera exponencial e variação aleatória (jitter).</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Tempo máximo de cada tentativa.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Tempo máximo da operação inteira, somando as tentativas e as esperas.</summary>
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>O circuito abre quando essa fração das chamadas falha dentro da janela de amostragem.</summary>
    public double CircuitFailureRatio { get; set; } = 0.5;

    public int CircuitMinimumThroughput { get; set; } = 5;

    public TimeSpan CircuitSamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Quanto tempo o circuito fica aberto, recusando chamadas sem tentar, antes de testar de novo.</summary>
    public TimeSpan CircuitBreakDuration { get; set; } = TimeSpan.FromSeconds(15);
}

/// <summary>
/// Cliente HTTP do gateway de pagamento. O <c>HttpClient</c> vem do <c>HttpClientFactory</c> com o pipeline de
/// resiliência; esta classe só traduz o protocolo do gateway para o <see cref="PaymentOutcome"/>.
/// </summary>
/// <remarks>
/// Toda cobrança leva o cabeçalho <c>Idempotency-Key</c>. É ele que torna seguro repetir um POST: se a primeira tentativa
/// cobrou e só a resposta se perdeu, a nova tentativa recebe a mesma transação, e não uma segunda cobrança.
/// </remarks>
public sealed partial class HttpPaymentGateway(HttpClient client, ILogger<HttpPaymentGateway> logger) : IPaymentGateway
{
    private sealed record ChargeBody(decimal Amount, string Currency, Guid Reference);

    private sealed record ChargeResponse(string? Id, string? Status, string? Reason);

    public async Task<PaymentOutcome> ChargeAsync(PaymentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var message = new HttpRequestMessage(HttpMethod.Post, "charges")
        {
            Content = JsonContent.Create(new ChargeBody(request.Amount, request.Currency, request.OrderId)),
        };
        message.Headers.Add("Idempotency-Key", request.IdempotencyKey);

        try
        {
            using var response = await client.SendAsync(message, cancellationToken);
            var body = await ReadBodyAsync(response, cancellationToken);

            if (response.IsSuccessStatusCode && body?.Id is { Length: > 0 } transactionId)
            {
                return PaymentOutcome.Approved(transactionId);
            }

            if (response.StatusCode == HttpStatusCode.PaymentRequired)
            {
                return PaymentOutcome.Declined(body?.Reason ?? "recusado pelo emissor");
            }

            LogUnexpected(logger, (int)response.StatusCode, request.OrderId);
            return PaymentOutcome.Unavailable($"resposta inesperada do gateway ({(int)response.StatusCode})");
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutRejectedException or BrokenCircuitException)
        {
            // Chegam aqui depois de esgotadas as novas tentativas, ou na hora, se o circuito estiver aberto.
            LogUnavailable(logger, request.OrderId, exception.GetType().Name, exception);
            return PaymentOutcome.Unavailable(exception is BrokenCircuitException ? "circuito aberto" : "gateway não respondeu");
        }
    }

    /// <summary>O corpo esperado do gateway, ou <c>null</c> se a resposta não for nesse formato (um 404 de proxy, um HTML).</summary>
    private static async Task<ChargeResponse?> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ChargeResponse>(cancellationToken);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or NotSupportedException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Gateway de pagamento indisponível para o pedido {OrderId} ({Reason})")]
    private static partial void LogUnavailable(ILogger logger, Guid orderId, string reason, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Gateway respondeu {StatusCode} inesperado para o pedido {OrderId}")]
    private static partial void LogUnexpected(ILogger logger, int statusCode, Guid orderId);
}

public static class PaymentSetup
{
    /// <summary>
    /// Registra o gateway com o pipeline de resiliência padrão do Microsoft.Extensions.Http.Resilience, nesta ordem
    /// (de fora para dentro): tempo total, nova tentativa, circuit breaker e tempo por tentativa.
    /// </summary>
    public static IServiceCollection AddPaymentGateway(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(PaymentOptions.SectionName).Get<PaymentOptions>() ?? new PaymentOptions();

        services.AddHttpClient<IPaymentGateway, HttpPaymentGateway>(client => client.BaseAddress = options.BaseUrl)
            .AddStandardResilienceHandler(resilience =>
            {
                resilience.TotalRequestTimeout.Timeout = options.TotalTimeout;
                resilience.AttemptTimeout.Timeout = options.AttemptTimeout;

                resilience.Retry.MaxRetryAttempts = options.MaxRetryAttempts;
                resilience.Retry.Delay = options.RetryBaseDelay;
                resilience.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
                resilience.Retry.UseJitter = true;

                resilience.CircuitBreaker.FailureRatio = options.CircuitFailureRatio;
                resilience.CircuitBreaker.MinimumThroughput = options.CircuitMinimumThroughput;
                resilience.CircuitBreaker.SamplingDuration = options.CircuitSamplingDuration;
                resilience.CircuitBreaker.BreakDuration = options.CircuitBreakDuration;
            });

        return services;
    }
}
