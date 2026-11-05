using Bifrost.Application.Abstractions;
using Bifrost.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bifrost.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<BifrostDbContext>(options => options
            .UseSqlite(connectionString)
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

        return services;
    }
}
