using Bifrost.Application;
using Bifrost.Infrastructure;
using Bifrost.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Bifrost")
    ?? throw new InvalidOperationException("A connection string 'Bifrost' não foi configurada.");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(connectionString);
builder.Services.AddHealthChecks();

var app = builder.Build();

// Só em desenvolvimento: em produção, as migrations rodam num passo próprio do deploy.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<BifrostDbContext>().Database.Migrate();
}

app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Ok(new { name = "Bifrost Orders API", status = "online" }));

app.Run();
