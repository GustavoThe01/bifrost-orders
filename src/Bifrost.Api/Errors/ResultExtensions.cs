using System.Diagnostics;
using Bifrost.Api.Observability;
using Bifrost.Application.Results;
using Microsoft.AspNetCore.Mvc;

namespace Bifrost.Api.Errors;

/// <summary>
/// Converte o <see cref="Result{T}"/> dos casos de uso em resposta HTTP. É o único lugar que conhece a tabela
/// entre categoria de erro e status: 400, 404, 409 e 422.
/// </summary>
internal static class ResultExtensions
{
    /// <summary>Sucesso vira 200 com o valor.</summary>
    public static ActionResult<T> ToActionResult<T>(this ControllerBase controller, Result<T> result)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(result);
        return result.Match<ActionResult<T>>(value => controller.Ok(value), error => controller.ToProblem(error));
    }

    /// <summary>Sucesso vira a resposta que o controller escolher, como um 201 com Location.</summary>
    public static ActionResult ToActionResult<T>(this ControllerBase controller, Result<T> result, Func<T, ActionResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(result);
        return result.Match(onSuccess, error => controller.ToProblem(error));
    }

    private static ObjectResult ToProblem(this ControllerBase controller, Error error)
    {
        var (status, type, title) = error.Type switch
        {
            ErrorType.Validation => (StatusCodes.Status400BadRequest, "validation", "Dados inválidos"),
            ErrorType.NotFound => (StatusCodes.Status404NotFound, "not-found", "Recurso não encontrado"),
            ErrorType.Conflict => (StatusCodes.Status409Conflict, "conflict", "Conflito"),
            ErrorType.BusinessRule => (StatusCodes.Status422UnprocessableEntity, "business-rule", "Regra de negócio violada"),
            _ => throw new ArgumentOutOfRangeException(nameof(error), error.Type, "Categoria de erro sem status HTTP."),
        };

        var problem = new ProblemDetails
        {
            Status = status,
            Type = "/problems/" + type,
            Title = title,
            Detail = error.Message,
            Instance = controller.HttpContext.Request.Path,
        };
        problem.Extensions["code"] = error.Code;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? controller.HttpContext.TraceIdentifier;
        problem.Extensions["correlationId"] = CorrelationIdMiddleware.Of(controller.HttpContext);
        if (error.Fields.Count > 0)
        {
            problem.Extensions["errors"] = error.Fields
                .Select(field => new { field = field.Field, message = field.Message })
                .ToList();
        }

        return new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" },
        };
    }
}
