using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bifrost.Api.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace Bifrost.IntegrationTests.Api;

/// <summary>Política "cliente só acessa os próprios pedidos": 401, 403, 404 e o administrador vendo tudo.</summary>
public sealed class OrderOwnershipTests : IAsyncLifetime
{
    private readonly string _connectionString = $"Data Source=owner-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly CancellationToken _token = TestContext.Current.CancellationToken;
    private readonly Guid _anaId = Guid.NewGuid();
    private readonly Guid _beaId = Guid.NewGuid();
    private SqliteConnection _keepAlive = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _ana = null!;
    private HttpClient _bea = null!;
    private HttpClient _admin = null!;
    private HttpClient _anonymous = null!;

    public async ValueTask InitializeAsync()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        await _keepAlive.OpenAsync(_token);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Bifrost", _connectionString));
        _ana = _factory.Customer(_anaId);
        _bea = _factory.Customer(_beaId);
        _admin = _factory.Admin();
        _anonymous = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in new[] { _ana, _bea, _admin, _anonymous })
        {
            client.Dispose();
        }

        await _factory.DisposeAsync();
        await _keepAlive.DisposeAsync();
    }

    private async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(_token));

    private async Task<string> CreateOrderAsync(HttpClient client)
    {
        var products = await client.GetFromJsonAsync<JsonElement>("/api/v1/products", _token);
        var body = new
        {
            shippingAddress = new { street = "Rua A", number = "1", city = "Fortaleza", state = "CE", zipCode = "60165121" },
            currency = "BRL",
            items = new[] { new { productId = products.EnumerateArray().First().GetProperty("id").GetString(), quantity = 1 } },
        };
        var response = await client.PostAsJsonAsync("/api/v1/orders", body, _token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Customer_SeesOwnOrder_ButAnotherCustomersOrderLooksLikeItDoesNotExist()
    {
        var anasOrder = await CreateOrderAsync(_ana);

        var byAna = await _ana.GetAsync($"/api/v1/orders/{anasOrder}", _token);
        var byBea = await _bea.GetAsync($"/api/v1/orders/{anasOrder}", _token);
        var nonexistent = await _bea.GetAsync($"/api/v1/orders/{Guid.NewGuid()}", _token);

        Assert.Equal(HttpStatusCode.OK, byAna.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, byBea.StatusCode);
        // A resposta para o pedido da Ana é igual à de um pedido que não existe, a não ser pelo id.
        var hidden = await JsonAsync(byBea);
        var missing = await JsonAsync(nonexistent);
        Assert.Equal(missing.GetProperty("code").GetString(), hidden.GetProperty("code").GetString());
        Assert.Equal(missing.GetProperty("type").GetString(), hidden.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Customer_CannotConfirmOrCancel_AnotherCustomersOrder()
    {
        var anasOrder = await CreateOrderAsync(_ana);

        var confirm = await _bea.PostAsync($"/api/v1/orders/{anasOrder}/confirm", null, _token);
        var cancel = await _bea.PostAsJsonAsync($"/api/v1/orders/{anasOrder}/cancel", new { reason = "Não é meu" }, _token);

        Assert.Equal(HttpStatusCode.NotFound, confirm.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, cancel.StatusCode);
        var order = await _ana.GetFromJsonAsync<JsonElement>($"/api/v1/orders/{anasOrder}", _token);
        Assert.Equal("Pending", order.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Customer_ListsOnlyOwnOrders_AndAskingForSomeoneElses_Is403()
    {
        await CreateOrderAsync(_ana);
        await CreateOrderAsync(_ana);
        await CreateOrderAsync(_bea);

        var anas = await _ana.GetFromJsonAsync<JsonElement>("/api/v1/orders", _token);
        var explicitOwn = await _ana.GetAsync($"/api/v1/orders?customerId={_anaId}", _token);
        var someoneElses = await _ana.GetAsync($"/api/v1/orders?customerId={_beaId}", _token);

        Assert.Equal(2, anas.GetProperty("totalItems").GetInt32());
        Assert.All(anas.GetProperty("items").EnumerateArray(), o => Assert.Equal(_anaId.ToString(), o.GetProperty("customerId").GetString()));
        Assert.Equal(HttpStatusCode.OK, explicitOwn.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, someoneElses.StatusCode);
        Assert.Equal("/problems/forbidden", (await JsonAsync(someoneElses)).GetProperty("type").GetString());
    }

    [Fact]
    public async Task CustomerWithoutOrders_GetsAnEmptyPage_NotAnError()
    {
        // Regressão: com o banco sem nenhum pedido, a primeira listagem vazia dava 500 (ver SummaryRow).
        var page = await _ana.GetAsync("/api/v1/orders", _token);

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var body = await JsonAsync(page);
        Assert.Equal(0, body.GetProperty("totalItems").GetInt32());
        Assert.Empty(body.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Admin_SeesAndChangesAnyOrder()
    {
        var anasOrder = await CreateOrderAsync(_ana);
        await CreateOrderAsync(_bea);

        var all = await _admin.GetFromJsonAsync<JsonElement>("/api/v1/orders", _token);
        var detail = await _admin.GetAsync($"/api/v1/orders/{anasOrder}", _token);
        var confirm = await _admin.PostAsync($"/api/v1/orders/{anasOrder}/confirm", null, _token);

        Assert.Equal(2, all.GetProperty("totalItems").GetInt32());
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
    }

    [Fact]
    public async Task Owner_CanConfirmAndCancel()
    {
        var anasOrder = await CreateOrderAsync(_ana);

        var confirm = await _ana.PostAsync($"/api/v1/orders/{anasOrder}/confirm", null, _token);
        var cancel = await _ana.PostAsJsonAsync($"/api/v1/orders/{anasOrder}/cancel", new { reason = "Mudei de ideia" }, _token);

        Assert.Equal("Confirmed", (await JsonAsync(confirm)).GetProperty("status").GetString());
        Assert.Equal("Cancelled", (await JsonAsync(cancel)).GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("GET", "/api/v1/orders")]
    [InlineData("GET", "/api/v1/orders/{id}")]
    [InlineData("POST", "/api/v1/orders/{id}/confirm")]
    [InlineData("POST", "/api/v1/orders/{id}/cancel")]
    public async Task EveryOrderRoute_WithoutToken_Is401(string method, string route)
    {
        var anasOrder = await CreateOrderAsync(_ana);
        using var request = new HttpRequestMessage(new HttpMethod(method), route.Replace("{id}", anasOrder, StringComparison.Ordinal));

        var response = await _anonymous.SendAsync(request, _token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnknownRole_GetsNothing()
    {
        // Token válido, mas sem perfil conhecido: não é dono de nada e não é administrador.
        using var stranger = _factory.ClientAs("Visitante", Guid.NewGuid());
        var anasOrder = await CreateOrderAsync(_ana);

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/v1/orders/{anasOrder}", _token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PostAsJsonAsync("/api/v1/products", new { sku = "Z-1" }, _token)).StatusCode);
        var list = await stranger.GetFromJsonAsync<JsonElement>("/api/v1/orders", _token);
        Assert.Equal(0, list.GetProperty("totalItems").GetInt32());
    }
}
