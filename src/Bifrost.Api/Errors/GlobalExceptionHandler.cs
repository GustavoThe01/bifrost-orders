using System.Diagnostics;
using Bifrost.Application;
using Bifrost.Domain.Common;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Bifrost.Api.Errors;

/// <summary>
/// Único lugar que transforma exceção em resposta HTTP. O corpo segue o padrão ProblemDetails (RFC 9457),
/// e um erro inesperado não revela nenhum detalhe interno a quem chamou.
/// </summary>
internal sealed partial class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            ValidationException validation => ValidationProblem(validation),
            NotFoundException => Problem(StatusCodes.Status404NotFound, "not-found", "Recurso não encontrado", exception.Message),
            ConflictException => Problem(StatusCodes.Status409Conflict, "conflict", "Conflito", exception.Message),
            DomainException => Problem(StatusCodes.Status422UnprocessableEntity, "business-rule", "Regra de negócio violada", exception.Message),
            _ => Problem(StatusCodes.Status500InternalServerError, "internal-error", "Erro interno", "Ocorreu um erro inesperado. Tente novamente mais tarde."),
        };

        if (problem.Status == StatusCodes.Status500InternalServerError)
        {
            LogUnexpected(logger, exception);
        }

        problem.Instance = httpContext.Request.Path;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = problem.Status!.Value;
        await httpContext.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken);
        return true;
    }

    private static ProblemDetails Problem(int status, string type, string title, string detail) => new()
    {
        Status = status,
        Type = "/problems/" + type,
        Title = title,
        Detail = detail,
    };

    private static ProblemDetails ValidationProblem(ValidationException exception)
    {
        var problem = Problem(StatusCodes.Status400BadRequest, "validation", "Dados inválidos", "Um ou mais campos não passaram na validação.");
        problem.Extensions["errors"] = exception.Errors
            .Select(error => new { field = error.PropertyName, message = error.ErrorMessage })
            .ToList();
        return problem;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Erro inesperado ao processar a requisição")]
    private static partial void LogUnexpected(ILogger logger, Exception exception);
}
