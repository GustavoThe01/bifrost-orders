using System.Text.RegularExpressions;
using Serilog.Context;

namespace Bifrost.Api.Observability;

/// <summary>
/// Dá a cada requisição um id de correlação: o que veio no cabeçalho <c>X-Correlation-Id</c>, se for válido,
/// ou um novo. O id volta na resposta e entra em todo log escrito durante a requisição.
/// </summary>
/// <remarks>
/// Com o id, dá para seguir um pedido do começo ao fim nos logs, mesmo com várias requisições ao mesmo tempo.
/// Quem chama (um front-end, outro serviço) pode mandar o próprio id para ligar os logs dos dois lados.
/// O valor recebido é validado: o cabeçalho vem de fora e vai parar nos logs, então só passa um formato simples
/// e curto, para ninguém injetar quebra de linha ou texto gigante no log.
/// </remarks>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var received = context.Request.Headers[HeaderName].ToString();
        var correlationId = IsValid(received) ? received : Guid.NewGuid().ToString("N");

        context.Items[ItemKey] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty(ItemKey, correlationId))
        {
            await next(context);
        }
    }

    /// <summary>O id da requisição em andamento, para incluir nas respostas de erro.</summary>
    public static string? Of(HttpContext context) => context?.Items[ItemKey] as string;

    private static bool IsValid(string value) => value.Length is > 0 and <= 64 && AllowedFormat().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex AllowedFormat();
}
