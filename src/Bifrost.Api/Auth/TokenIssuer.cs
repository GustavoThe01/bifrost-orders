using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Bifrost.Api.Auth;

public sealed record IssuedToken(string AccessToken, DateTimeOffset ExpiresAt);

/// <summary>Emite tokens JWT assinados com HMAC-SHA256 e confere as contas de demonstração.</summary>
public sealed class TokenIssuer(
    IOptions<JwtOptions> jwtOptions,
    IOptions<List<DemoAccount>> accounts,
    TimeProvider clock)
{
    private static readonly PasswordHasher<DemoAccount> Hasher = new();

    // Hash de uma senha qualquer, conferido quando o e-mail não existe. Assim o tempo de resposta é o mesmo
    // para e-mail desconhecido e para senha errada, e não revela quais contas existem.
    private static readonly string DummyHash = Hasher.HashPassword(new DemoAccount(), "bifrost-sem-conta");

    /// <summary>Confere e-mail e senha. Devolve a conta ou <c>null</c>, sem dizer qual dos dois errou.</summary>
    public DemoAccount? Authenticate(string email, string password)
    {
        var account = accounts.Value.FirstOrDefault(a => string.Equals(a.Email, email?.Trim(), StringComparison.OrdinalIgnoreCase));
        var result = Hasher.VerifyHashedPassword(account ?? new DemoAccount(), account?.PasswordHash ?? DummyHash, password ?? string.Empty);
        return account is not null && result != PasswordVerificationResult.Failed ? account : null;
    }

    public IssuedToken Issue(Guid subjectId, string email, string role)
    {
        var options = jwtOptions.Value;
        var now = clock.GetUtcNow();
        var expiresAt = now.AddMinutes(options.LifetimeMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, subjectId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim("role", role),
            ]),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(options.SigningKeyBytes()),
                SecurityAlgorithms.HmacSha256),
        };

        return new IssuedToken(new JsonWebTokenHandler().CreateToken(descriptor), expiresAt);
    }
}
