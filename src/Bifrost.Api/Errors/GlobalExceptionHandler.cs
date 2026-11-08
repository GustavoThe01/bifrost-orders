using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Bifrost.Api.Errors;

/// <summary>
/// Rede de segurança para o que ninguém esperava. Falhas de negócio não chegam aqui: elas voltam como
/// <c>Result</c> e viram resposta em <see cref="ResultExtensions"/>. O que sobra é bug ou infraestrutura fora do ar,
/// e por isso a resposta é sempre 500, sem nenhum detalhe interno.
/// </summary>
internal sealed partial class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        LogUnexpected(logger, exception);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Type = "/problems/internal-error",
            Title = "Erro interno",
            Detail = "Ocorreu um erro inesperado. Tente novamente mais tarde.",
            Instance = httpContext.Request.Path,
        };
        problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Erro inesperado ao processar a requisição")]
    private static partial void LogUnexpected(ILogger logger, Exception exception);
}
