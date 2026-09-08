using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;

namespace EntraAuth.AspNetCore;

public sealed class ScopeOrAppRoleHandler : AuthorizationHandler<ScopeOrAppRoleRequirement>
{
    private static readonly string[] ScopeClaimTypes =
    [
        "scp",
        "http://schemas.microsoft.com/identity/claims/scope",
    ];

    private static readonly string[] RoleClaimTypes =
    [
        "roles",
        ClaimTypes.Role,
        "http://schemas.microsoft.com/ws/2008/06/identity/claims/role",
    ];

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ScopeOrAppRoleRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);
        var scopes = GetValues(context.User, ScopeClaimTypes)
            .SelectMany(value => value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var roles = GetValues(context.User, RoleClaimTypes);

        var scopeMatch = requirement.Scopes.Any(required =>
            scopes.Contains(required, StringComparer.Ordinal));
        var roleMatch = requirement.AppRoles.Any(required =>
            roles.Contains(required, StringComparer.Ordinal));

        if (scopeMatch || roleMatch)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    private static IEnumerable<string> GetValues(ClaimsPrincipal user, IEnumerable<string> claimTypes)
    {
        foreach (var type in claimTypes)
        {
            foreach (var claim in user.FindAll(type))
            {
                if (!string.IsNullOrWhiteSpace(claim.Value))
                {
                    yield return claim.Value;
                }
            }
        }
    }
}
