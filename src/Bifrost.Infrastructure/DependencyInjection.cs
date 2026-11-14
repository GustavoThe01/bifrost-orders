using Bifrost.Application.Abstractions;
using Bifrost.Infrastructure.Outbox;
using Bifrost.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bifrost.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString) =>
        services.AddInfrastructure(_ => connectionString);

    /// <summary>Lê a connection string só quando o contexto é criado, o que permite aos testes da API trocá-la.</summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        Func<IServiceProvider, string> connectionString)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<OutboxInterceptor>();
        services.AddDbContext<BifrostDbContext>((provider, options) => options
            .UseSqlite(connectionString(provider))
            .AddInterceptors(provider.GetRequiredService<OutboxInterceptor>())
            .UseSeeding((context, _) => CatalogSeed.Seed(context))
            .UseAsyncSeeding((context, _, _) =>
            {
                CatalogSeed.Seed(context);
                return Task.CompletedTask;
            }));

        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IOrderQueries, OrderQueries>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.TryAddSingleton<IOutboxPublisher, LoggingOutboxPublisher>();
        services.AddScoped<OutboxDispatcher>();

        return services;
    }
}
