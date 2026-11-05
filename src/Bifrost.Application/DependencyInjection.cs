using Bifrost.Application.Messaging;
using Bifrost.Application.Orders;
using Bifrost.Domain.Orders;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bifrost.Application;

public static class DependencyInjection
{
    /// <remarks>
    /// Cada handler é registrado à mão, sem varrer o assembly. Fica mais longo, mas um handler esquecido
    /// ou com o tipo errado aparece aqui, e a ordem do pipeline fica visível no código.
    /// </remarks>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<CreateOrderValidator>(ServiceLifetime.Scoped, includeInternalTypes: true);
        services.AddScoped<IDispatcher, Dispatcher>();

        services.AddCommand<CreateOrderCommand, Guid, CreateOrderHandler>();
        services.AddCommand<ConfirmOrderCommand, OrderStatus, ConfirmOrderHandler>();
        services.AddCommand<CancelOrderCommand, OrderStatus, CancelOrderHandler>();

        services.AddQuery<GetOrderByIdQuery, OrderDto, GetOrderByIdHandler>();
        services.AddQuery<ListProductsQuery, IReadOnlyList<ProductDto>, ListProductsHandler>();

        return services;
    }

    /// <summary>Pipeline de escrita: log, depois validação, depois o handler.</summary>
    private static void AddCommand<TCommand, TResult, THandler>(this IServiceCollection services)
        where TCommand : ICommand<TResult>
        where THandler : class, ICommandHandler<TCommand, TResult>
    {
        services.AddScoped<THandler>();
        services.AddScoped<ICommandHandler<TCommand, TResult>>(provider =>
            new LoggingCommandDecorator<TCommand, TResult>(
                new ValidationCommandDecorator<TCommand, TResult>(
                    provider.GetRequiredService<THandler>(),
                    provider.GetServices<IValidator<TCommand>>()),
                provider.GetRequiredService<ILogger<LoggingCommandDecorator<TCommand, TResult>>>()));
    }

    /// <summary>Pipeline de leitura: só log.</summary>
    private static void AddQuery<TQuery, TResult, THandler>(this IServiceCollection services)
        where TQuery : IQuery<TResult>
        where THandler : class, IQueryHandler<TQuery, TResult>
    {
        services.AddScoped<THandler>();
        services.AddScoped<IQueryHandler<TQuery, TResult>>(provider =>
            new LoggingQueryDecorator<TQuery, TResult>(
                provider.GetRequiredService<THandler>(),
                provider.GetRequiredService<ILogger<LoggingQueryDecorator<TQuery, TResult>>>()));
    }
}
