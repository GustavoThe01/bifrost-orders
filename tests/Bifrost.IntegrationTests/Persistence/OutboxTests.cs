using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bifrost.Domain.Catalog;
using Bifrost.Domain.Orders;
using Bifrost.Domain.ValueObjects;
using Bifrost.Infrastructure.Outbox;
using Bifrost.Infrastructure.Persistence;
using Bifrost.IntegrationTests.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bifrost.IntegrationTests.Persistence;

/// <summary>Outbox: gravado na mesma transação do pedido e publicado depois, em segundo plano.</summary>
public sealed class OutboxTests : IAsyncLifetime
{
    private readonly string _connectionString = $"Data Source=outbox-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly CancellationToken _token = TestContext.Current.CancellationToken;
    private readonly RecordingPublisher _publisher = new();
    private SqliteConnection _keepAlive = null!;
    private WebApplicationFactory<Program> _factory = null!;

    /// <summary>Guarda o que foi publicado. Falha nas mensagens cujo tipo estiver em <see cref="FailFor"/>.</summary>
    private sealed class RecordingPublisher : IOutboxPublisher
    {
        public ConcurrentQueue<OutboxMessage> Published { get; } = new();

        public HashSet<string> FailFor { get; } = [];

        public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            if (FailFor.Contains(message.Type))
            {
                throw new InvalidOperationException("Broker fora do ar");
            }

            Published.Enqueue(message);
            return Task.CompletedTask;
        }
    }

    public async ValueTask InitializeAsync()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        await _keepAlive.OpenAsync(_token);
        _factory = Factory(enabled: false);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _keepAlive.DisposeAsync();
    }

    private WebApplicationFactory<Program> Factory(bool enabled) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseSetting("ConnectionStrings:Bifrost", _connectionString);
        builder.UseSetting("Outbox:Enabled", enabled ? "true" : "false");
        builder.UseSetting("Outbox:PollingInterval", "00:00:00.100");
        builder.ConfigureTestServices(services => services.AddSingleton<IOutboxPublisher>(_publisher));
    });

    private async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider);
    }

    private Task<List<OutboxMessage>> MessagesAsync() =>
        InScope(s => s.GetRequiredService<BifrostDbContext>().OutboxMessages.AsNoTracking().OrderBy(m => m.OccurredAt).ThenBy(m => m.Id).ToListAsync(_token));

    private async Task<string> CreateOrderAsync(HttpClient client)
    {
        var products = await client.GetFromJsonAsync<JsonElement>("/api/v1/products", _token);
        var body = new
        {
            shippingAddress = new { street = "Rua A", number = "1", city = "Fortaleza", state = "CE", zipCode = "60165121" },
            currency = "BRL",
            items = new[] { new { productId = products.EnumerateArray().First(p => p.GetProperty("sku").GetString() == "CAN-001").GetProperty("id").GetString(), quantity = 2 } },
        };
        var response = await client.PostAsJsonAsync("/api/v1/orders", body, _token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(_token)).GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task EachChange_WritesItsEvent_InTheSameSaveChanges()
    {
        using var client = _factory.Customer();
        var id = await CreateOrderAsync(client);
        await client.PostAsync($"/api/v1/orders/{id}/confirm", null, _token);
        await client.PostAsJsonAsync($"/api/v1/orders/{id}/cancel", new { reason = "Desistência" }, _token);

        var messages = await MessagesAsync();

        Assert.Equal(["OrderCreated", "OrderConfirmed", "OrderCancelled"], messages.Select(m => m.Type));
        Assert.All(messages, m => Assert.Null(m.ProcessedAt));
        var confirmed = JsonSerializer.Deserialize<JsonElement>(messages[1].Payload);
        Assert.Equal(id, confirmed.GetProperty("orderId").GetString());
        Assert.Equal(79.80m, confirmed.GetProperty("total").GetDecimal());
        var cancelled = JsonSerializer.Deserialize<JsonElement>(messages[2].Payload);
        Assert.Equal("Confirmed", cancelled.GetProperty("previousStatus").GetString());
    }

    [Fact]
    public async Task RejectedOperation_WritesNoEvent()
    {
        using var client = _factory.Customer();
        var id = await CreateOrderAsync(client);
        await client.PostAsJsonAsync($"/api/v1/orders/{id}/cancel", new { reason = "Desistência" }, _token);

        var rejected = await client.PostAsync($"/api/v1/orders/{id}/confirm", null, _token); // cancelado não confirma

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        Assert.Equal(["OrderCreated", "OrderCancelled"], (await MessagesAsync()).Select(m => m.Type));
    }

    [Fact]
    public async Task WhenTheSaveFails_NeitherTheOrderNorTheEventIsWritten()
    {
        // Um pedido novo e um produto com SKU repetido no mesmo SaveChanges: o índice único derruba a transação inteira.
        var failed = await InScope(async s =>
        {
            var context = s.GetRequiredService<BifrostDbContext>();
            var existing = await context.Products.FirstAsync(_token);
            var order = Order.Create(CustomerId.New(), Address.Create("Rua A", "1", "Fortaleza", "CE", "60165121"), "BRL");
            order.AddItem(existing.Id, existing.Name, existing.Price, 1);
            context.Orders.Add(order);
            context.Products.Add(Product.Create(existing.Sku, "Duplicado", Money.Create(1m, "BRL"), 1));
            try
            {
                await context.SaveChangesAsync(_token);
                return false;
            }
            catch (DbUpdateException)
            {
                return true;
            }
        });

        Assert.True(failed);
        Assert.Empty(await MessagesAsync());
        Assert.Equal(0, await InScope(s => s.GetRequiredService<BifrostDbContext>().Orders.CountAsync(_token)));
    }

    [Fact]
    public async Task Dispatcher_PublishesInOrder_AndMarksAsProcessed()
    {
        using var client = _factory.Customer();
        var id = await CreateOrderAsync(client);
        await client.PostAsync($"/api/v1/orders/{id}/confirm", null, _token);

        var published = await InScope(s => s.GetRequiredService<OutboxDispatcher>().ProcessBatchAsync(new OutboxOptions(), _token));
        var again = await InScope(s => s.GetRequiredService<OutboxDispatcher>().ProcessBatchAsync(new OutboxOptions(), _token));

        Assert.Equal(2, published);
        Assert.Equal(0, again);
        Assert.Equal(["OrderCreated", "OrderConfirmed"], _publisher.Published.Select(m => m.Type));
        Assert.All(await MessagesAsync(), m => Assert.NotNull(m.ProcessedAt));
    }

    [Fact]
    public async Task FailedPublish_IsRetried_UntilTheLimit_WithoutBlockingTheOthers()
    {
        using var client = _factory.Customer();
        var id = await CreateOrderAsync(client);
        await client.PostAsync($"/api/v1/orders/{id}/confirm", null, _token);
        _publisher.FailFor.Add("OrderCreated");
        var options = new OutboxOptions { MaxAttempts = 2 };

        for (var round = 0; round < 3; round++)
        {
            await InScope(s => s.GetRequiredService<OutboxDispatcher>().ProcessBatchAsync(options, _token));
        }

        var messages = await MessagesAsync();
        var created = messages.Single(m => m.Type == "OrderCreated");
        Assert.Null(created.ProcessedAt);
        Assert.Equal(2, created.Attempts); // parou no limite: a terceira rodada não tentou de novo
        Assert.Equal("Broker fora do ar", created.LastError);
        Assert.NotNull(messages.Single(m => m.Type == "OrderConfirmed").ProcessedAt);
    }

    [Fact]
    public async Task BackgroundService_PublishesOnItsOwn()
    {
        await _factory.DisposeAsync();
        _factory = Factory(enabled: true);
        using var client = _factory.Customer();

        await CreateOrderAsync(client);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (_publisher.Published.IsEmpty && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, _token);
        }

        Assert.Equal("OrderCreated", Assert.Single(_publisher.Published).Type);
        Assert.NotNull(Assert.Single(await MessagesAsync()).ProcessedAt);
    }
}
