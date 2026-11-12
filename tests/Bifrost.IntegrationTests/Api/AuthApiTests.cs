using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Bifrost.Api.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Bifrost.IntegrationTests.Api;

/// <summary>Emissão e validação do JWT, perfis Admin e Customer, e as respostas 401 e 403.</summary>
public sealed class AuthApiTests : IAsyncLifetime
{
    private readonly string _connectionString = $"Data Source=auth-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly CancellationToken _token = TestContext.Current.CancellationToken;
    private SqliteConnection _keepAlive = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public async ValueTask InitializeAsync()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        await _keepAlive.OpenAsync(_token);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Bifrost", _connectionString));
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _keepAlive.DisposeAsync();
    }

    private async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(_token));

    private async Task<HttpResponseMessage> RequestTokenAsync(string email, string password)
    {
        using var client = _factory.CreateClient();
        return await client.PostAsJsonAsync("/api/v1/auth/token", new { email, password }, _token);
    }

    private static object ProductBody(string sku) => new { sku, name = "Novidade", price = 10m, currency = "BRL", stockQuantity = 1 };

    private async Task<object> OrderBodyAsync(HttpClient client, object? extra = null)
    {
        var products = await client.GetFromJsonAsync<JsonElement>("/api/v1/products", _token);
        var productId = products.EnumerateArray().First().GetProperty("id").GetString();
        return new
        {
            customerId = extra, // ignorado pela API: o cliente vem do token
            shippingAddress = new { street = "Rua A", number = "1", city = "Fortaleza", state = "CE", zipCode = "60165121" },
            currency = "BRL",
            items = new[] { new { productId, quantity = 1 } },
        };
    }

    /// <summary>Monta um token à mão, para testar o que o emissor da API nunca geraria.</summary>
    private string HandMadeToken(Action<SecurityTokenDescriptor> change, byte[]? key = null)
    {
        var options = _factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(10),
            Subject = new ClaimsIdentity([new Claim("sub", TestAuth.AdminId.ToString()), new Claim("role", Roles.Admin)]),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key ?? options.SigningKeyBytes()), SecurityAlgorithms.HmacSha256),
        };
        change(descriptor);
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private async Task<HttpStatusCode> CreateProductWithAsync(string token)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await client.PostAsJsonAsync("/api/v1/products", ProductBody("TOK-" + Guid.NewGuid().ToString("N")[..8]), _token)).StatusCode;
    }

    [Fact]
    public async Task DemoAccount_GetsASignedToken_WithTheRoleAndSubject()
    {
        var response = await RequestTokenAsync("ADMIN@bifrost.test", "admin-demo-2026");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal("Bearer", body.GetProperty("tokenType").GetString());
        Assert.InRange(body.GetProperty("expiresIn").GetInt32(), 3590, 3600);

        var jwt = new JsonWebToken(body.GetProperty("accessToken").GetString());
        Assert.Equal("HS256", jwt.Alg);
        Assert.Equal("bifrost-orders", jwt.Issuer);
        Assert.Equal("bifrost-orders-api", Assert.Single(jwt.Audiences));
        Assert.Equal(TestAuth.AdminId.ToString(), jwt.Subject);
        Assert.Equal(Roles.Admin, jwt.GetClaim("role").Value);
    }

    [Fact]
    public async Task WrongPassword_AndUnknownEmail_GetTheSame401()
    {
        var wrongPassword = await RequestTokenAsync("admin@bifrost.test", "senha-errada");
        var unknownEmail = await RequestTokenAsync("ninguem@bifrost.test", "admin-demo-2026");

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        var first = await JsonAsync(wrongPassword);
        var second = await JsonAsync(unknownEmail);
        Assert.Equal("/problems/invalid-credentials", first.GetProperty("type").GetString());
        Assert.Equal(first.GetProperty("detail").GetString(), second.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Catalog_IsOpen_ButCreatingAProduct_IsAdminOnly()
    {
        using var anonymous = _factory.CreateClient();
        using var customer = _factory.Customer();
        using var admin = _factory.Admin();

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/v1/products", _token)).StatusCode);

        var withoutToken = await anonymous.PostAsJsonAsync("/api/v1/products", ProductBody("ADM-001"), _token);
        var asCustomer = await customer.PostAsJsonAsync("/api/v1/products", ProductBody("ADM-001"), _token);
        var asAdmin = await admin.PostAsJsonAsync("/api/v1/products", ProductBody("ADM-001"), _token);

        Assert.Equal(HttpStatusCode.Unauthorized, withoutToken.StatusCode);
        Assert.Equal("Bearer", withoutToken.Headers.WwwAuthenticate.Single().Scheme);
        Assert.Equal("/problems/unauthorized", (await JsonAsync(withoutToken)).GetProperty("type").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, asCustomer.StatusCode);
        Assert.Equal("/problems/forbidden", (await JsonAsync(asCustomer)).GetProperty("type").GetString());
        Assert.Equal(HttpStatusCode.Created, asAdmin.StatusCode);
    }

    [Fact]
    public async Task Orders_RequireAToken_AndOnlyCustomersPlaceThem()
    {
        using var anonymous = _factory.CreateClient();
        using var admin = _factory.Admin();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/orders", _token)).StatusCode);
        var byAdmin = await admin.PostAsJsonAsync("/api/v1/orders", await OrderBodyAsync(admin), _token);
        Assert.Equal(HttpStatusCode.Forbidden, byAdmin.StatusCode);
    }

    [Fact]
    public async Task TheOrderBelongsToTheTokenOwner_NotToWhatTheBodySays()
    {
        // Ponta a ponta: login da conta de demonstração, e o token dela no pedido.
        var login = await JsonAsync(await RequestTokenAsync("cliente@bifrost.test", "cliente-demo-2026"));
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("accessToken").GetString());

        var created = await client.PostAsJsonAsync("/api/v1/orders", await OrderBodyAsync(client, extra: Guid.NewGuid()), _token);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await JsonAsync(created)).GetProperty("id").GetString();

        var order = await client.GetFromJsonAsync<JsonElement>($"/api/v1/orders/{id}", _token);
        Assert.Equal(TestAuth.CustomerId.ToString(), order.GetProperty("customerId").GetString());
    }

    [Fact]
    public async Task ExpiredToken_IsRejected()
    {
        var expired = HandMadeToken(d =>
        {
            d.IssuedAt = DateTime.UtcNow.AddHours(-2);
            d.NotBefore = DateTime.UtcNow.AddHours(-2);
            d.Expires = DateTime.UtcNow.AddMinutes(-5); // passou da tolerância de 30 s
        });

        Assert.Equal(HttpStatusCode.Unauthorized, await CreateProductWithAsync(expired));
    }

    [Fact]
    public async Task TokenFromAnotherKey_OrForAnotherApi_IsRejected()
    {
        var otherKey = HandMadeToken(_ => { }, key: new byte[32]);
        var otherAudience = HandMadeToken(d => d.Audience = "outra-api");
        var otherIssuer = HandMadeToken(d => d.Issuer = "outro-emissor");

        Assert.Equal(HttpStatusCode.Unauthorized, await CreateProductWithAsync(otherKey));
        Assert.Equal(HttpStatusCode.Unauthorized, await CreateProductWithAsync(otherAudience));
        Assert.Equal(HttpStatusCode.Unauthorized, await CreateProductWithAsync(otherIssuer));
        Assert.Equal(HttpStatusCode.Created, await CreateProductWithAsync(HandMadeToken(_ => { })));
    }

    [Fact]
    public async Task TamperedToken_IsRejected()
    {
        // Troca o perfil no payload sem refazer a assinatura: um cliente tentando virar administrador.
        var customerToken = TestAuth.TokenFor(_factory, Roles.Customer, TestAuth.CustomerId);
        var parts = customerToken.Split('.');
        var payload = Base64UrlEncoder.Decode(parts[1]).Replace("\"Customer\"", "\"Admin\"", StringComparison.Ordinal);
        var tampered = $"{parts[0]}.{Base64UrlEncoder.Encode(payload)}.{parts[2]}";

        Assert.Equal(HttpStatusCode.Unauthorized, await CreateProductWithAsync(tampered));
    }

    [Fact]
    public async Task WithoutAStrongSigningKey_TheApiDoesNotStart()
    {
        await using var factory = _factory.WithWebHostBuilder(builder => builder.UseSetting("Jwt:SigningKey", "curta"));

        var error = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains("32 bytes", error.Message, StringComparison.Ordinal);
    }
}
