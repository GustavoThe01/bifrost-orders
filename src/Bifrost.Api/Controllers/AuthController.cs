using System.Diagnostics;
using Bifrost.Api.Auth;
using Bifrost.Api.Observability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bifrost.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[AllowAnonymous]
public sealed class AuthController(TokenIssuer issuer, TimeProvider clock) : ControllerBase
{
    public sealed record TokenRequest(string Email, string Password)
    {
        /// <summary>A senha nunca aparece no ToString de um record, que pode acabar num log.</summary>
        public override string ToString() => $"TokenRequest {{ Email = {Email} }}";
    }

    public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn);

    /// <summary>Troca e-mail e senha de uma conta de demonstração por um token de acesso.</summary>
    [HttpPost("token")]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public ActionResult<TokenResponse> Token(TokenRequest request)
    {
        var account = issuer.Authenticate(request.Email, request.Password);
        if (account is null)
        {
            // Mesma resposta para e-mail desconhecido e senha errada.
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Type = "/problems/invalid-credentials",
                Title = "Credenciais inválidas",
                Detail = "E-mail ou senha inválidos.",
                Instance = HttpContext.Request.Path,
            };
            problem.Extensions["code"] = "auth.invalid_credentials";
            problem.Extensions["traceId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
            problem.Extensions["correlationId"] = CorrelationIdMiddleware.Of(HttpContext);
            return new ObjectResult(problem) { StatusCode = StatusCodes.Status401Unauthorized, ContentTypes = { "application/problem+json" } };
        }

        var token = issuer.Issue(account.SubjectId, account.Email, account.Role);
        var expiresIn = (int)Math.Round((token.ExpiresAt - clock.GetUtcNow()).TotalSeconds);
        return Ok(new TokenResponse(token.AccessToken, "Bearer", expiresIn));
    }
}
