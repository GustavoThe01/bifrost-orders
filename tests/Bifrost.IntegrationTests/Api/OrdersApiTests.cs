using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace Bifrost.IntegrationTests.Api;

/// <summary>Sobe a API inteira em memória: HTTP, pipeline, EF Core e SQLite com a migration e o seed reais.</summary>
public sealed class OrdersApiTests : IAsyncLifetime
{
    private readonly string _connectionString = $"Data Source=api-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly CancellationToken _token = TestContext.Current.CancellationToken;
    private SqliteConnection _keepAlive = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        await _keepAlive.OpenAsync(_token);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Bifrost", _connectionString));
        _client = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _keepAlive.DisposeAsync();
    }

    private async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(_token));

    private async Task<string> FirstProductIdAsync()
    {
        var products = await _client.GetFromJsonAsync<JsonElement>("/api/v1/products", _token);
        return products.EnumerateArray().First(p => p.GetProperty("sku").GetString() == "CAN-001").GetProperty("id").GetString()!;
    }

    private static object OrderBody(string productId, int quantity = 2) => new
    {
        customerId = Guid.NewGuid(),
        shippingAddress = new { street = "Av. Beira Mar", number = "100", city = "Fortaleza", state = "CE", zipCode = "60165121" },
        currency = "BRL",
        items = new[] { new { productId, quantity } },
    };

    private async Task<string> CreateOrderAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/orders", OrderBody(await FirstProductIdAsync()), _token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("id").GetString()!;
    }

    private async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string type)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await JsonAsync(response);
        Assert.Equal("/problems/" + type, problem.GetProperty("type").GetString());
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task Products_ListsTheSeededCatalog()
    {
        var products = await _client.GetFromJsonAsync<JsonElement>("/api/v1/products", _token);

        Assert.Equal(5, products.GetArrayLength());
    }

    [Fact]
    public async Task Products_CreatesNew_AndRejectsRepeatedSku()
    {
        var body = new { sku = "NOV-001", name = "Novidade", price = 10.50m, currency = "BRL", stockQuantity = 3 };

        var created = await _client.PostAsJsonAsync("/api/v1/products", body, _token);
        var repeated = await _client.PostAsJsonAsync("/api/v1/products", body, _token);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await AssertProblemAsync(repeated, HttpStatusCode.Conflict, "conflict");
    }

    [Fact]
    public async Task Order_GoesThroughTheWholeFlow()
    {
        var id = await CreateOrderAsync();

        var order = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/orders/{id}", _token);
        Assert.Equal("Pending", order.GetProperty("status").GetString());
        Assert.Equal(79.80m, order.GetProperty("total").GetDecimal());

        var confirmed = await _client.PostAsync($"/api/v1/orders/{id}/confirm", null, _token);
        Assert.Equal("Confirmed", (await JsonAsync(confirmed)).GetProperty("status").GetString());

        var cancelled = await _client.PostAsJsonAsync($"/api/v1/orders/{id}/cancel", new { reason = "Desistência" }, _token);
        Assert.Equal("Cancelled", (await JsonAsync(cancelled)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task InvalidOrder_Returns400_ListingTheFields()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/orders", OrderBody(Guid.NewGuid().ToString(), quantity: 0), _token);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");
    }

    [Fact]
    public async Task UnknownProduct_Returns404()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/orders", OrderBody(Guid.NewGuid().ToString()), _token);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "not-found");
    }

    [Fact]
    public async Task UnknownOrder_Returns404()
    {
        var response = await _client.GetAsync($"/api/v1/orders/{Guid.NewGuid()}", _token);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "not-found");
    }

    [Fact]
    public async Task BusinessRuleViolation_Returns422()
    {
        var id = await CreateOrderAsync();
        await _client.PostAsJsonAsync($"/api/v1/orders/{id}/cancel", new { reason = "Desistência" }, _token);

        var response = await _client.PostAsync($"/api/v1/orders/{id}/confirm", null, _token);

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "business-rule");
    }

    [Fact]
    public async Task ListOrders_ReturnsAPage()
    {
        var id = await CreateOrderAsync();

        var page = await _client.GetFromJsonAsync<JsonElement>("/api/v1/orders?pageSize=5", _token);

        Assert.Equal(1, page.GetProperty("totalItems").GetInt32());
        Assert.Equal(5, page.GetProperty("pageSize").GetInt32());
        var order = Assert.Single(page.GetProperty("items").EnumerateArray().ToList());
        Assert.Equal(id, order.GetProperty("id").GetString());
        Assert.Equal(79.80m, order.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task ListOrders_InvalidFilters_Return400()
    {
        await AssertProblemAsync(await _client.GetAsync("/api/v1/orders?status=Arquivado", _token), HttpStatusCode.BadRequest, "validation");
        await AssertProblemAsync(await _client.GetAsync("/api/v1/orders?pageSize=1000", _token), HttpStatusCode.BadRequest, "validation");
    }

    [Fact]
    public async Task MalformedJson_Returns400()
    {
        using var content = new StringContent("{ isto não é json", System.Text.Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/v1/orders", content, _token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task OpenApi_DocumentsTheEndpoints()
    {
        var document = await _client.GetFromJsonAsync<JsonElement>("/openapi/v1.json", _token);

        Assert.True(document.GetProperty("paths").TryGetProperty("/api/v1/orders", out _));
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/scalar/v1", _token)).StatusCode);
    }

    [Fact]
    public async Task Health_IsUp()
    {
        Assert.Equal("Healthy", await _client.GetStringAsync("/health", _token));
    }
}
