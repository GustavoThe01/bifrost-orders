using Bifrost.Api.Errors;
using Bifrost.Application;
using Bifrost.Infrastructure;
using Bifrost.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(services =>
    services.GetRequiredService<IConfiguration>().GetConnectionString("Bifrost")
    ?? throw new InvalidOperationException("A connection string 'Bifrost' não foi configurada."));

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Só em desenvolvimento: em produção, as migrations rodam num passo próprio do deploy,
// e a documentação interativa não fica exposta.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<BifrostDbContext>().Database.Migrate();

    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapControllers();
app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Ok(new { name = "Bifrost Orders API", status = "online" })).ExcludeFromDescription();

app.Run();
