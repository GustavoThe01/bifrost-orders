using System.Diagnostics;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Bifrost.Application.Messaging;

/// <summary>Valida o command antes do handler. Se for inválido, o handler nem chega a rodar.</summary>
internal sealed class ValidationCommandDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    IEnumerable<IValidator<TCommand>> validators) : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    public async Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        foreach (var validator in validators)
        {
            await validator.ValidateAndThrowAsync(command, cancellationToken);
        }

        return await inner.HandleAsync(command, cancellationToken);
    }
}

/// <summary>Valida a query antes do handler, do mesmo jeito que os commands.</summary>
internal sealed class ValidationQueryDecorator<TQuery, TResult>(
    IQueryHandler<TQuery, TResult> inner,
    IEnumerable<IValidator<TQuery>> validators) : IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    public async Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken)
    {
        foreach (var validator in validators)
        {
            await validator.ValidateAndThrowAsync(query, cancellationToken);
        }

        return await inner.HandleAsync(query, cancellationToken);
    }
}

/// <summary>Registra o nome do command, a duração e a falha, sem gravar o conteúdo, que pode ter dados pessoais.</summary>
internal sealed class LoggingCommandDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    ILogger<LoggingCommandDecorator<TCommand, TResult>> logger) : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    public Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken) =>
        PipelineLogging.MeasureAsync(logger, typeof(TCommand).Name, () => inner.HandleAsync(command, cancellationToken));
}

internal sealed class LoggingQueryDecorator<TQuery, TResult>(
    IQueryHandler<TQuery, TResult> inner,
    ILogger<LoggingQueryDecorator<TQuery, TResult>> logger) : IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    public Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken) =>
        PipelineLogging.MeasureAsync(logger, typeof(TQuery).Name, () => inner.HandleAsync(query, cancellationToken));
}

internal static partial class PipelineLogging
{
    public static async Task<TResult> MeasureAsync<TResult>(ILogger logger, string name, Func<Task<TResult>> next)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            var result = await next();
            Completed(logger, name, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Message} falhou em {ElapsedMs:0.0} ms")]
    private static partial void Failed(ILogger logger, Exception exception, string message, double elapsedMs);
}
