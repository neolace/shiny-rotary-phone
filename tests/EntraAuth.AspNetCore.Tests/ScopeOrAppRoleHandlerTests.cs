using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;

namespace EntraAuth.AspNetCore.Tests;

public sealed class ScopeOrAppRoleHandlerTests
{
    [Fact]
    public async Task Succeeds_when_scp_contains_required_scope()
    {
        var context = await HandleAsync(
            User(("scp", "Orders.Read")),
            scopes: ["Orders.Read"],
            roles: ["Orders.Read.All"]);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Succeeds_when_space_separated_scp_includes_required_scope()
    {
        var context = await HandleAsync(
            User(("scp", "Orders.Read Orders.Write")),
            scopes: ["Orders.Write"],
            roles: []);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Succeeds_when_mapped_scope_claim_matches()
    {
        var context = await HandleAsync(
            User(("http://schemas.microsoft.com/identity/claims/scope", "Orders.Read")),
            scopes: ["Orders.Read"],
            roles: []);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Succeeds_when_roles_claim_matches_app_role()
    {
        var context = await HandleAsync(
            User(("roles", "Orders.Read.All")),
            scopes: ["Orders.Read"],
            roles: ["Orders.Read.All"]);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Succeeds_when_claim_types_role_matches()
    {
        var context = await HandleAsync(
            User((ClaimTypes.Role, "Orders.Write.All")),
            scopes: [],
            roles: ["Orders.Write.All"]);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Fails_when_scope_differs_only_by_case()
    {
        var context = await HandleAsync(
            User(("scp", "orders.read")),
            scopes: ["Orders.Read"],
            roles: []);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Fails_when_token_has_neither_scope_nor_role()
    {
        var context = await HandleAsync(
            User(("scp", "Orders.Read")),
            scopes: ["Orders.Write"],
            roles: ["Orders.Write.All"]);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Fails_when_requirement_has_no_scopes_or_roles()
    {
        var context = await HandleAsync(User(("scp", "Orders.Read")), scopes: [], roles: []);

        Assert.False(context.HasSucceeded);
    }

    private static ClaimsPrincipal User(params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity(
            claims.Select(c => new Claim(c.Type, c.Value)),
            authenticationType: "Test");
        return new ClaimsPrincipal(identity);
    }

    private static async Task<AuthorizationHandlerContext> HandleAsync(
        ClaimsPrincipal user,
        IEnumerable<string> scopes,
        IEnumerable<string> roles)
    {
        var requirement = new ScopeOrAppRoleRequirement(scopes, roles);
        var context = new AuthorizationHandlerContext([requirement], user, resource: null);
        await new ScopeOrAppRoleHandler().HandleAsync(context);
        return context;
    }
}
