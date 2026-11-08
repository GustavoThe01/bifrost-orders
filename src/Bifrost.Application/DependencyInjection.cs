using Bifrost.Application.Catalog;
using Bifrost.Application.Messaging;
using Bifrost.Application.Orders;
using Bifrost.Application.Results;
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

        services.AddCommand<CreateProductCommand, Result<Guid>, CreateProductHandler>();
        services.AddCommand<CreateOrderCommand, Result<Guid>, CreateOrderHandler>();
        services.AddCommand<ConfirmOrderCommand, Result<OrderStatus>, ConfirmOrderHandler>();
        services.AddCommand<CancelOrderCommand, Result<OrderStatus>, CancelOrderHandler>();

        services.AddQuery<GetOrderByIdQuery, Result<OrderDto>, GetOrderByIdHandler>();
        services.AddQuery<ListOrdersQuery, Result<PagedResult<OrderSummaryDto>>, ListOrdersHandler>();
        services.AddQuery<ListProductsQuery, Result<IReadOnlyList<ProductDto>>, ListProductsHandler>();

        return services;
    }

    /// <summary>
    /// Pipeline de escrita: log, validação, conversão de regra de negócio violada em <see cref="Result{T}"/> e o handler.
    /// </summary>
    private static void AddCommand<TCommand, TResult, THandler>(this IServiceCollection services)
        where TCommand : ICommand<TResult>
        where TResult : IFailureFactory<TResult>
        where THandler : class, ICommandHandler<TCommand, TResult>
    {
        services.AddScoped<THandler>();
        services.AddScoped<ICommandHandler<TCommand, TResult>>(provider =>
            new LoggingCommandDecorator<TCommand, TResult>(
                new ValidationCommandDecorator<TCommand, TResult>(
                    new BusinessRuleCommandDecorator<TCommand, TResult>(provider.GetRequiredService<THandler>()),
                    provider.GetServices<IValidator<TCommand>>()),
                provider.GetRequiredService<ILogger<LoggingCommandDecorator<TCommand, TResult>>>()));
    }

    /// <summary>Pipeline de leitura: log, depois validação. Queries não abrem transação.</summary>
    private static void AddQuery<TQuery, TResult, THandler>(this IServiceCollection services)
        where TQuery : IQuery<TResult>
        where TResult : IFailureFactory<TResult>
        where THandler : class, IQueryHandler<TQuery, TResult>
    {
        services.AddScoped<THandler>();
        services.AddScoped<IQueryHandler<TQuery, TResult>>(provider =>
            new LoggingQueryDecorator<TQuery, TResult>(
                new ValidationQueryDecorator<TQuery, TResult>(
                    provider.GetRequiredService<THandler>(),
                    provider.GetServices<IValidator<TQuery>>()),
                provider.GetRequiredService<ILogger<LoggingQueryDecorator<TQuery, TResult>>>()));
    }
}
