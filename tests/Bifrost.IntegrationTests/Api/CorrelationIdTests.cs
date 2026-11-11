using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Bifrost.Api.Observability;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;

namespace Bifrost.IntegrationTests.Api;

/// <summary>Id de correlação na resposta, nos erros e nos logs estruturados.</summary>
public sealed class CorrelationIdTests : IAsyncLifetime
{
    private readonly string _connectionString = $"Data Source=correlacao-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly CancellationToken _token = TestContext.Current.CancellationToken;
    private readonly CollectingSink _logs = new();
    private SqliteConnection _keepAlive = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    /// <summary>Destino de log em memória. O Program o encontra pelo ReadFrom.Services do Serilog.</summary>
    private sealed class CollectingSink : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }

    public async ValueTask InitializeAsync()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        await _keepAlive.OpenAsync(_token);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Bifrost", _connectionString);
            builder.ConfigureTestServices(services => services.AddSingleton<ILogEventSink>(_logs));
        });
        _client = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _keepAlive.DisposeAsync();
    }

    private async Task<HttpResponseMessage> GetAsync(string path, string? correlationId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (correlationId is not null)
        {
            request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, correlationId);
        }

        return await _client.SendAsync(request, _token);
    }

    private static string? HeaderOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var values) ? values.Single() : null;

    [Fact]
    public async Task ReceivedId_GoesBackInTheResponse()
    {
        var response = await GetAsync("/api/v1/products", "pedido-123.app_web");

        Assert.Equal("pedido-123.app_web", HeaderOf(response));
    }

    [Fact]
    public async Task WithoutHeader_ANewIdIsCreated()
    {
        var first = HeaderOf(await GetAsync("/api/v1/products"));
        var second = HeaderOf(await GetAsync("/api/v1/products"));

        Assert.Matches("^[0-9a-f]{32}$", first!);
        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("tem espaço")]
    [InlineData("<script>")]
    public async Task InvalidId_IsReplaced_SoItCannotPolluteTheLogs(string invalid)
    {
        Assert.Matches("^[0-9a-f]{32}$", HeaderOf(await GetAsync("/api/v1/products", invalid))!);
        Assert.Matches("^[0-9a-f]{32}$", HeaderOf(await GetAsync("/api/v1/products", new string('a', 65)))!);
    }

    [Fact]
    public async Task Problems_CarryTheCorrelationId()
    {
        var response = await GetAsync($"/api/v1/orders/{Guid.NewGuid()}", "erro-404");

        var problem = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(_token));
        Assert.Equal("erro-404", problem.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task EveryLogOfTheRequest_HasTheCorrelationIdAsAProperty()
    {
        await GetAsync("/api/v1/products", "rastreio-42");

        var events = _logs.Events.Where(e => e.Properties.TryGetValue("CorrelationId", out var value)
            && value.ToString() == "\"rastreio-42\"").ToList();

        // O log do pipeline de queries (handler) e o log da requisição HTTP, os dois com o mesmo id.
        Assert.Contains(events, e => e.MessageTemplate.Text.Contains("concluído", StringComparison.Ordinal));
        var request = Assert.Single(events, e => e.MessageTemplate.Text.StartsWith("HTTP", StringComparison.Ordinal));
        Assert.Equal("200", request.Properties["StatusCode"].ToString());
    }

    [Fact]
    public async Task Logs_AreStructured_NotJustText()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders")
        {
            Content = JsonContent.Create(new { customerId = Guid.Empty, currency = "BRL", items = Array.Empty<object>(),
                shippingAddress = new { street = "Rua A", number = "1", city = "Fortaleza", state = "CE", zipCode = "60165121" } }),
        };
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "validacao-1");
        await _client.SendAsync(request, _token);

        var rejected = Assert.Single(_logs.Events, e => e.MessageTemplate.Text.Contains("recusado", StringComparison.Ordinal)
            && e.Properties["CorrelationId"].ToString() == "\"validacao-1\"");
        Assert.Equal("\"validation\"", rejected.Properties["Code"].ToString());
        Assert.True(rejected.Properties.ContainsKey("ElapsedMs"));
    }
}
