using System.Net.Http.Headers;
using Bifrost.Api.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Bifrost.IntegrationTests.Api;

/// <summary>Clientes HTTP já autenticados, com tokens emitidos pelo próprio emissor da API sob teste.</summary>
internal static class TestAuth
{
    public static readonly Guid AdminId = Guid.Parse("a0000000-0000-4000-8000-000000000001");
    public static readonly Guid CustomerId = Guid.Parse("c0000000-0000-4000-8000-000000000001");

    public static string TokenFor(WebApplicationFactory<Program> factory, string role, Guid subject) =>
        factory.Services.GetRequiredService<TokenIssuer>().Issue(subject, $"{subject:N}@bifrost.test", role).AccessToken;

    public static HttpClient ClientAs(this WebApplicationFactory<Program> factory, string role, Guid subject)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenFor(factory, role, subject));
        return client;
    }

    public static HttpClient Customer(this WebApplicationFactory<Program> factory, Guid? id = null) =>
        factory.ClientAs(Roles.Customer, id ?? CustomerId);

    public static HttpClient Admin(this WebApplicationFactory<Program> factory) => factory.ClientAs(Roles.Admin, AdminId);
}
