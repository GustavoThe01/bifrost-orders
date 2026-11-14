using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bifrost.Application.Payments;
using Bifrost.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bifrost.IntegrationTests.Api;

/// <summary>
/// Pagamento com o pipeline de resiliência de verdade (retry, timeout e circuit breaker). Só a rede é trocada: um
/// servidor de pagamento falso responde o que cada teste mandar.
/// </summary>
public sealed class PaymentTests : IAsyncLifetime
{
    private readonly string _connectionString = $"Data Source=pagamento-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly CancellationToken _token = TestContext.Current.CancellationToken;
    private readonly FakeGateway _gateway = new();
    private SqliteConnection _keepAlive = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    /// <summary>Servidor de pagamento falso: responde em sequência e registra cada chamada.</summary>
    private sealed class FakeGateway : HttpMessageHandler
    {
        private readonly ConcurrentQueue<Func<CancellationToken, Task<HttpResponseMessage>>> _script = new();

        public ConcurrentQueue<string> IdempotencyKeys { get; } = new();

        public int Calls => IdempotencyKeys.Count;

        /// <summary>Resposta usada quando o roteiro acaba.</summary>
        public Func<CancellationToken, Task<HttpResponseMessage>> Default { get; set; } = _ => Task.FromResult(Approved());

        public void Then(params Func<CancellationToken, Task<HttpResponseMessage>>[] responses)
        {
            foreach (var response in responses)
            {
                _script.Enqueue(response);
            }
        }

        public static HttpResponseMessage Approved() =>
            new(HttpStatusCode.Created) { Content = JsonContent.Create(new { id = "pay_abc123", status = "approved" }) };

        public static Func<CancellationToken, Task<HttpResponseMessage>> Status(HttpStatusCode status, object? body = null) =>
            _ => Task.FromResult(new HttpResponseMessage(status) { Content = JsonContent.Create(body ?? new { }) });

        public static Func<CancellationToken, Task<HttpResponseMessage>> Slow(TimeSpan delay) => async token =>
        {
            await Task.Delay(delay, token);
            return Approved();
        };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            IdempotencyKeys.Enqueue(request.Headers.GetValues("Idempotency-Key").Single());
            return (_script.TryDequeue(out var next) ? next : Default)(cancellationToken);
        }
    }

    public async ValueTask InitializeAsync()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        await _keepAlive.OpenAsync(_token);
        _factory = Factory();
        _client = _factory.Customer();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _keepAlive.DisposeAsync();
    }

    /// <summary>Tempos curtos para o teste não esperar segundos; a lógica do pipeline é a mesma da produção.</summary>
    private WebApplicationFactory<Program> Factory(int circuitMinimumThroughput = 100, bool fakeNetwork = true) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Bifrost", _connectionString);
            builder.UseSetting("Outbox:Enabled", "false");
            builder.UseSetting("Payments:BaseUrl", "http://gateway.test/");
            builder.UseSetting("Payments:MaxRetryAttempts", "3");
            builder.UseSetting("Payments:RetryBaseDelay", "00:00:00.010");
            builder.UseSetting("Payments:AttemptTimeout", "00:00:00.300");
            builder.UseSetting("Payments:TotalTimeout", "00:00:05");
            builder.UseSetting("Payments:CircuitMinimumThroughput", circuitMinimumThroughput.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("Payments:CircuitSamplingDuration", "00:00:10");
            builder.UseSetting("Payments:CircuitBreakDuration", "00:00:30");
            if (fakeNetwork)
            {
                builder.ConfigureTestServices(services =>
                    services.AddHttpClient(nameof(IPaymentGateway)).ConfigurePrimaryHttpMessageHandler(() => _gateway));
            }
        });

    private async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(_token));

    private async Task<string> ConfirmedOrderAsync(HttpClient client)
    {
        var products = await client.GetFromJsonAsync<JsonElement>("/api/v1/products", _token);
        var body = new
        {
            shippingAddress = new { street = "Rua A", number = "1", city = "Fortaleza", state = "CE", zipCode = "60165121" },
            currency = "BRL",
            items = new[] { new { productId = products.EnumerateArray().First(p => p.GetProperty("sku").GetString() == "CAN-001").GetProperty("id").GetString(), quantity = 2 } },
        };
        var id = (await JsonAsync(await client.PostAsJsonAsync("/api/v1/orders", body, _token))).GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/orders/{id}/confirm", null, _token)).StatusCode);
        return id;
    }

    private Task<HttpResponseMessage> PayAsync(string id, HttpClient? client = null) =>
        (client ?? _client).PostAsync($"/api/v1/orders/{id}/pay", null, _token);

    private async Task<string> StatusOfAsync(string id) =>
        (await _client.GetFromJsonAsync<JsonElement>($"/api/v1/orders/{id}", _token)).GetProperty("status").GetString()!;

    [Fact]
    public async Task Approved_MarksAsPaid_AndRecordsTheTransactionInTheEvent()
    {
        var id = await ConfirmedOrderAsync(_client);

        var response = await PayAsync(id);

        Assert.Equal("Paid", (await JsonAsync(response)).GetProperty("status").GetString());
        Assert.Equal($"order-{id}", Assert.Single(_gateway.IdempotencyKeys));
        var paid = await Scoped(context => context.OutboxMessages.SingleAsync(m => m.Type == "OrderPaid", _token));
        Assert.Contains("pay_abc123", paid.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TransientFailures_AreRetried_WithTheSameIdempotencyKey()
    {
        var id = await ConfirmedOrderAsync(_client);
        _gateway.Then(FakeGateway.Status(HttpStatusCode.ServiceUnavailable), FakeGateway.Status(HttpStatusCode.InternalServerError));

        var response = await PayAsync(id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, _gateway.Calls);
        Assert.Single(_gateway.IdempotencyKeys.Distinct()); // três tentativas, uma cobrança só no gateway
    }

    [Fact]
    public async Task GatewayDown_Gives503_AfterTheRetries_AndNothingChanges()
    {
        var id = await ConfirmedOrderAsync(_client);
        _gateway.Default = FakeGateway.Status(HttpStatusCode.ServiceUnavailable);

        var response = await PayAsync(id);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("payment.unavailable", (await JsonAsync(response)).GetProperty("code").GetString());
        Assert.Equal(4, _gateway.Calls); // a primeira tentativa e mais 3
        Assert.Equal("Confirmed", await StatusOfAsync(id));
    }

    [Fact]
    public async Task Declined_Is422_AndIsNotRetried()
    {
        var id = await ConfirmedOrderAsync(_client);
        _gateway.Then(FakeGateway.Status(HttpStatusCode.PaymentRequired, new { status = "declined", reason = "saldo insuficiente" }));

        var response = await PayAsync(id);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await JsonAsync(response);
        Assert.Equal("payment.declined", problem.GetProperty("code").GetString());
        Assert.Contains("saldo insuficiente", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(1, _gateway.Calls);
    }

    [Fact]
    public async Task SlowGateway_HitsTheAttemptTimeout_AndTheNextAttemptSucceeds()
    {
        var id = await ConfirmedOrderAsync(_client);
        _gateway.Then(FakeGateway.Slow(TimeSpan.FromSeconds(3)));

        var response = await PayAsync(id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, _gateway.Calls);
    }

    [Fact]
    public async Task AfterRepeatedFailures_TheCircuitOpens_AndCallsFailFast_WithoutReachingTheGateway()
    {
        await _factory.DisposeAsync();
        _factory = Factory(circuitMinimumThroughput: 4);
        using var client = _factory.Customer();
        _gateway.Default = FakeGateway.Status(HttpStatusCode.ServiceUnavailable);
        var first = await ConfirmedOrderAsync(client);
        var second = await ConfirmedOrderAsync(client);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await PayAsync(first, client)).StatusCode);
        var callsWhenTheCircuitOpened = _gateway.Calls;
        var started = DateTime.UtcNow;
        var fastFail = await PayAsync(second, client);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, fastFail.StatusCode);
        Assert.Equal(callsWhenTheCircuitOpened, _gateway.Calls); // o circuito aberto nem chamou o gateway
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task OnlyAConfirmedOrder_CanBePaid_AndTheGatewayIsNotCalledOtherwise()
    {
        var products = await _client.GetFromJsonAsync<JsonElement>("/api/v1/products", _token);
        var pending = (await JsonAsync(await _client.PostAsJsonAsync("/api/v1/orders", new
        {
            shippingAddress = new { street = "Rua A", number = "1", city = "Fortaleza", state = "CE", zipCode = "60165121" },
            currency = "BRL",
            items = new[] { new { productId = products.EnumerateArray().First().GetProperty("id").GetString(), quantity = 1 } },
        }, _token))).GetProperty("id").GetString()!;

        var response = await PayAsync(pending);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("order.not_payable", (await JsonAsync(response)).GetProperty("code").GetString());
        Assert.Equal(0, _gateway.Calls);
    }

    [Fact]
    public async Task UnexpectedResponse_IsUnavailable_NotAnError500()
    {
        var id = await ConfirmedOrderAsync(_client);
        _gateway.Default = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("<html>não encontrado</html>", System.Text.Encoding.UTF8, "text/html"),
        });

        var response = await PayAsync(id);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, _gateway.Calls); // 404 não é falha passageira: não tenta de novo
        Assert.Equal("Confirmed", await StatusOfAsync(id));
    }

    [Fact]
    public async Task AnotherCustomersOrder_IsNotFound_AndIsNotCharged()
    {
        var id = await ConfirmedOrderAsync(_client);
        using var someoneElse = _factory.Customer(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, (await PayAsync(id, someoneElse)).StatusCode);
        Assert.Equal(0, _gateway.Calls);
    }

    [Fact]
    public async Task Simulator_EndToEnd_ApprovesAndKeepsTheSameTransactionForTheSameKey()
    {
        // Sem a rede falsa: a API chama o próprio simulador, servido em desenvolvimento.
        await using var factory = Factory(fakeNetwork: false).WithWebHostBuilder(builder => builder
            .UseSetting("Payments:BaseUrl", "http://gateway.test/simulador/pagamentos/")
            .ConfigureTestServices(services => services.AddHttpClient(nameof(IPaymentGateway))
                .ConfigurePrimaryHttpMessageHandler(provider => provider.GetRequiredService<IServer>() is TestServer server
                    ? server.CreateHandler()
                    : throw new InvalidOperationException("Sem TestServer"))));
        using var raw = factory.CreateClient();
        using var customer = factory.Customer();

        var charge = new { amount = 79.80m, currency = "BRL", reference = Guid.NewGuid() };
        HttpRequestMessage Request() => new(HttpMethod.Post, "/simulador/pagamentos/charges")
        {
            Content = JsonContent.Create(charge),
            Headers = { { "Idempotency-Key", "order-teste" } },
        };
        var first = await JsonAsync(await raw.SendAsync(Request(), _token));
        var again = await JsonAsync(await raw.SendAsync(Request(), _token));
        Assert.Equal(first.GetProperty("id").GetString(), again.GetProperty("id").GetString());

        var id = await ConfirmedOrderAsync(customer);
        var paid = await customer.PostAsync($"/api/v1/orders/{id}/pay", null, _token);
        Assert.Equal("Paid", (await JsonAsync(paid)).GetProperty("status").GetString());
    }

    private async Task<T> Scoped<T>(Func<BifrostDbContext, Task<T>> work)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<BifrostDbContext>());
    }
}
