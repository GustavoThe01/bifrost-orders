using System.Diagnostics;
using Bifrost.Application.Abstractions;
using Bifrost.Application.Results;
using Bifrost.Domain.Common;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Bifrost.Application.Messaging;

/// <summary>
/// Valida a mensagem antes do handler. Se for inválida, devolve uma falha de validação e o handler nem roda.
/// </summary>
internal static class Validation
{
    public static async Task<Error?> ValidateAsync<TMessage>(
        IEnumerable<IValidator<TMessage>> validators,
        TMessage message,
        CancellationToken cancellationToken)
    {
        var fields = new List<FieldError>();
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(message, cancellationToken);
            fields.AddRange(result.Errors.Select(error => new FieldError(error.PropertyName, error.ErrorMessage)));
        }

        return fields.Count == 0 ? null : Error.Validation(fields);
    }
}

internal sealed class ValidationCommandDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    IEnumerable<IValidator<TCommand>> validators) : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
    where TResult : IFailureFactory<TResult>
{
    public async Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken) =>
        await Validation.ValidateAsync(validators, command, cancellationToken) is { } error
            ? TResult.Failure(error)
            : await inner.HandleAsync(command, cancellationToken);
}

internal sealed class ValidationQueryDecorator<TQuery, TResult>(
    IQueryHandler<TQuery, TResult> inner,
    IEnumerable<IValidator<TQuery>> validators) : IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
    where TResult : IFailureFactory<TResult>
{
    public async Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken) =>
        await Validation.ValidateAsync(validators, query, cancellationToken) is { } error
            ? TResult.Failure(error)
            : await inner.HandleAsync(query, cancellationToken);
}

/// <summary>
/// Fronteira entre o domínio e a aplicação. O domínio protege as invariantes lançando <see cref="DomainException"/>;
/// aqui essa exceção vira uma falha de regra de negócio, e daí para fora só existe <see cref="Result{T}"/>.
/// </summary>
/// <remarks>
/// A transação não chega a ser confirmada: a exceção acontece antes do <c>SaveChangesAsync</c> do handler.
/// </remarks>
internal sealed class BusinessRuleCommandDecorator<TCommand, TResult>(ICommandHandler<TCommand, TResult> inner)
    : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
    where TResult : IFailureFactory<TResult>
{
    public async Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        try
        {
            return await inner.HandleAsync(command, cancellationToken);
        }
        catch (DomainException exception)
        {
            return TResult.Failure(Error.BusinessRule("business_rule", exception.Message));
        }
        catch (ConcurrencyConflictException)
        {
            return TResult.Failure(Error.Conflict(
                "concurrency_conflict",
                "Os dados mudaram enquanto a operação acontecia. Nada foi gravado; tente de novo."));
        }
    }
}

/// <summary>Registra o nome do command, a duração e o resultado, sem gravar o conteúdo, que pode ter dados pessoais.</summary>
internal sealed class LoggingCommandDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    ILogger<LoggingCommandDecorator<TCommand, TResult>> logger) : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
    where TResult : IResult
{
    public Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken) =>
        PipelineLogging.MeasureAsync(logger, typeof(TCommand).Name, () => inner.HandleAsync(command, cancellationToken));
}

internal sealed class LoggingQueryDecorator<TQuery, TResult>(
    IQueryHandler<TQuery, TResult> inner,
    ILogger<LoggingQueryDecorator<TQuery, TResult>> logger) : IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
    where TResult : IResult
{
    public Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken) =>
        PipelineLogging.MeasureAsync(logger, typeof(TQuery).Name, () => inner.HandleAsync(query, cancellationToken));
}

internal static partial class PipelineLogging
{
    public static async Task<TResult> MeasureAsync<TResult>(ILogger logger, string name, Func<Task<TResult>> next)
        where TResult : IResult
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            var result = await next();
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (result.IsFailure)
            {
                Rejected(logger, name, result.Error!.Code, elapsed);
            }
            else
            {
                Completed(logger, name, elapsed);
            }

            return result;
        }
        catch (Exception exception)
        {
            Failed(logger, exception, name, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Message} concluído em {ElapsedMs:0.0} ms")]
    private static partial void Completed(ILogger logger, string message, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Message} recusado ({Code}) em {ElapsedMs:0.0} ms")]
    private static partial void Rejected(ILogger logger, string message, string code, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Message} falhou em {ElapsedMs:0.0} ms")]
    private static partial void Failed(ILogger logger, Exception exception, string message, double elapsedMs);
}
