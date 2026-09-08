using Microsoft.AspNetCore.Authorization;

namespace EntraAuth.AspNetCore;

public sealed class ScopeOrAppRoleRequirement : IAuthorizationRequirement
{
    public ScopeOrAppRoleRequirement(IEnumerable<string> scopes, IEnumerable<string> appRoles)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(appRoles);
        Scopes = scopes.ToArray();
        AppRoles = appRoles.ToArray();
    }

    public IReadOnlyList<string> Scopes { get; }
    public IReadOnlyList<string> AppRoles { get; }
}
