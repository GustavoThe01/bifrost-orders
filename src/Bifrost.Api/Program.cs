using System.Security.Cryptography;
using Bifrost.Api.Auth;
using Bifrost.Api.Errors;
using Bifrost.Api.Observability;
using Bifrost.Application;
using Bifrost.Infrastructure;
using Bifrost.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

// Logs estruturados: cada propriedade (CorrelationId, ElapsedMs, Message...) vira um campo, e não só texto.
// Em desenvolvimento, uma linha legível; fora dele, JSON compacto, pronto para uma ferramenta de busca de logs.
// ReadFrom.Services acrescenta os destinos registrados no DI; os testes usam isso para ler os logs.
// preserveStaticLogger: cada host tem o próprio logger, sem trocar o Log.Logger estático do processo.
// Sem isso, dois hosts no mesmo processo (como nos testes em paralelo) misturariam os logs um do outro.
builder.Services.AddSerilog(preserveStaticLogger: true, configureLogger: (services, logger) =>
{
    logger.ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();

    if (builder.Environment.IsDevelopment())
    {
        logger.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}");
    }
    else
    {
        logger.WriteTo.Console(new RenderedCompactJsonFormatter());
    }
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(services =>
    services.GetRequiredService<IConfiguration>().GetConnectionString("Bifrost")
    ?? throw new InvalidOperationException("A connection string 'Bifrost' não foi configurada."));

// Em desenvolvimento (e nos testes), a API tem contas de demonstração e, se nenhuma chave foi configurada,
// gera uma chave de assinatura nova a cada execução: os tokens deixam de valer quando a API reinicia.
// Fora de desenvolvimento não há contas de demonstração, e a chave é obrigatória (Jwt__SigningKey).
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddJsonFile("demo-accounts.json", optional: false);
    if (string.IsNullOrWhiteSpace(builder.Configuration["Jwt:SigningKey"]))
    {
        builder.Configuration["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }
}

builder.Services.AddBifrostAuthentication(builder.Configuration);
builder.Services.AddControllers();

// 401 e 403 saem do middleware de autenticação sem corpo; o UseStatusCodePages os completa com ProblemDetails.
// Aqui eles ganham o mesmo formato dos outros erros da API.
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    var problem = context.ProblemDetails;
    if (problem.Status == StatusCodes.Status401Unauthorized && problem.Type?.StartsWith("https://", StringComparison.Ordinal) != false)
    {
        problem.Type = "/problems/unauthorized";
        problem.Title = "Não autenticado";
        problem.Detail = "Envie um token válido no cabeçalho Authorization: Bearer <token>.";
    }
    else if (problem.Status == StatusCodes.Status403Forbidden && problem.Type?.StartsWith("https://", StringComparison.Ordinal) != false)
    {
        problem.Type = "/problems/forbidden";
        problem.Title = "Sem permissão";
        problem.Detail = "O seu perfil não pode fazer esta operação.";
    }

    problem.Extensions.TryAdd("correlationId", CorrelationIdMiddleware.Of(context.HttpContext));
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} respondeu {StatusCode} em {Elapsed:0.0} ms";

    // Sem isso, o log da requisição iria para o Log.Logger estático, que fica mudo com preserveStaticLogger.
    options.Logger = app.Services.GetRequiredService<Serilog.ILogger>();
});
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

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
