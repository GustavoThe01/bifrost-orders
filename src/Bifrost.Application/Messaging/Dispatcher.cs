using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Bifrost.Application.Messaging;

/// <summary>
/// Dispatcher próprio (ADR 0002). Quem chama só conhece o tipo do resultado, então o tipo concreto do
/// command é resolvido em tempo de execução. A reflexão acontece uma vez por tipo e fica em cache.
/// </summary>
internal sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    private static readonly ConcurrentDictionary<(Type Message, Type Result), object> Invokers = new();

    public Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var invoker = (IInvoker<TResult>)Invokers.GetOrAdd(
            (command.GetType(), typeof(TResult)),
            static key => Activator.CreateInstance(typeof(CommandInvoker<,>).MakeGenericType(key.Message, key.Result))!);

        return invoker.InvokeAsync(services, command, cancellationToken);
    }

    public Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var invoker = (IInvoker<TResult>)Invokers.GetOrAdd(
            (query.GetType(), typeof(TResult)),
            static key => Activator.CreateInstance(typeof(QueryInvoker<,>).MakeGenericType(key.Message, key.Result))!);

        return invoker.InvokeAsync(services, query, cancellationToken);
    }

    private interface IInvoker<TResult>
    {
        Task<TResult> InvokeAsync(IServiceProvider services, object message, CancellationToken cancellationToken);
    }

    private sealed class CommandInvoker<TCommand, TResult> : IInvoker<TResult>
        where TCommand : ICommand<TResult>
    {
        public Task<TResult> InvokeAsync(IServiceProvider services, object message, CancellationToken cancellationToken) =>
            services.GetRequiredService<ICommandHandler<TCommand, TResult>>().HandleAsync((TCommand)message, cancellationToken);
    }

    private sealed class QueryInvoker<TQuery, TResult> : IInvoker<TResult>
        where TQuery : IQuery<TResult>
    {
        public Task<TResult> InvokeAsync(IServiceProvider services, object message, CancellationToken cancellationToken) =>
            services.GetRequiredService<IQueryHandler<TQuery, TResult>>().HandleAsync((TQuery)message, cancellationToken);
    }
}
