using System.ComponentModel.DataAnnotations;

namespace Bifrost.Api.Auth;

/// <summary>Configuração do JWT (seção <c>Jwt</c>). Conferida na partida: sem chave válida, a API não sobe.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Quem emite o token. A validação recusa token de outro emissor.</summary>
    [Required]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Para quem o token foi emitido. A validação recusa token feito para outra API.</summary>
    [Required]
    public string Audience { get; set; } = string.Empty;

    [Range(1, 1440)]
    public int LifetimeMinutes { get; set; } = 60;

    /// <summary>
    /// Chave HMAC-SHA256 em Base64, com pelo menos 32 bytes. Nunca no appsettings.json de produção:
    /// vem de variável de ambiente (<c>Jwt__SigningKey</c>) ou de um cofre de segredos.
    /// </summary>
    [Required]
    public string SigningKey { get; set; } = string.Empty;

    public byte[] SigningKeyBytes() => Convert.FromBase64String(SigningKey);

    public bool HasStrongKey()
    {
        try
        {
            return SigningKeyBytes().Length >= 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>Os dois perfis da API.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Customer = "Customer";
}

/// <summary>
/// Conta de demonstração (seção <c>DemoAccounts</c>). Só existe para desenvolvimento e testes: em produção, quem
/// emite o token seria um provedor de identidade (Entra ID, Keycloak), e a API só validaria.
/// </summary>
public sealed class DemoAccount
{
    public string Email { get; set; } = string.Empty;

    /// <summary>Hash PBKDF2 no formato do <c>PasswordHasher</c> do ASP.NET Core Identity. A senha nunca fica salva.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    /// <summary>Vai no claim <c>sub</c>. Para um cliente, é o id usado nos pedidos.</summary>
    public Guid SubjectId { get; set; }
}
