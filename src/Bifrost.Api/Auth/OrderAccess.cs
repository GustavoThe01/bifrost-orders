using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Bifrost.Api.Auth;

/// <summary>Nomes das políticas de autorização da API.</summary>
public static class Policies
{
    /// <summary>Administrador, ou o cliente dono do pedido. O recurso avaliado é o id do cliente do pedido.</summary>
    public const string OrderOwner = "order-owner";
}

/// <summary>Quem chamou pode ver e mexer no pedido deste cliente?</summary>
public sealed class OrderOwnerRequirement : IAuthorizationRequirement;

/// <summary>
/// Autorização baseada no recurso: a decisão depende do pedido, e não só do perfil. Por isso ela roda depois de o
/// pedido ser lido, e não num atributo <c>[Authorize]</c>, que só enxerga o token.
/// </summary>
public sealed class OrderOwnerHandler : AuthorizationHandler<OrderOwnerRequirement, Guid>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OrderOwnerRequirement requirement, Guid customerId)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.User.IsInRole(Roles.Admin) || context.User.SubjectId() == customerId)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public static class ClaimsPrincipalExtensions
{
    /// <summary>O claim <c>sub</c> do token como Guid, ou <c>null</c> se não houver.</summary>
    public static Guid? SubjectId(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Guid.TryParse(user.FindFirst("sub")?.Value, out var id) ? id : null;
    }
}
