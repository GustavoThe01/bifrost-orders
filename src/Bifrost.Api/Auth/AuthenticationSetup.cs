using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Bifrost.Api.Auth;

public static class AuthenticationSetup
{
    /// <summary>
    /// Autenticação por JWT no cabeçalho <c>Authorization: Bearer</c>. A API valida assinatura, emissor, público
    /// e validade de cada token, sem sessão e sem consultar banco.
    /// </summary>
    public static IServiceCollection AddBifrostAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => options.HasStrongKey(), "Jwt:SigningKey precisa ser Base64 com pelo menos 32 bytes.")
            .ValidateOnStart();
        services.Configure<List<DemoAccount>>(configuration.GetSection("DemoAccounts"));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<TokenIssuer>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Os parâmetros saem das mesmas opções validadas, e não de uma segunda leitura da configuração.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                var options = jwt.Value;

                // Sem o mapeamento antigo da Microsoft: "sub" continua "sub", e "role" continua "role".
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(options.SigningKeyBytes()),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "sub",
                    RoleClaimType = "role",
                };
            });

        services.AddSingleton<IAuthorizationHandler, OrderOwnerHandler>();
        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.OrderOwner, policy => policy.RequireAuthenticatedUser().AddRequirements(new OrderOwnerRequirement()));
        return services;
    }
}
