namespace EntraAuth.AspNetCore.Tests;

public sealed class ScopeOrAppRoleRequirementTests
{
    [Fact]
    public void Copies_scopes_and_app_roles()
    {
        var requirement = new ScopeOrAppRoleRequirement(["Orders.Read"], ["Orders.Read.All"]);

        Assert.Equal(["Orders.Read"], requirement.Scopes);
        Assert.Equal(["Orders.Read.All"], requirement.AppRoles);
    }

    [Fact]
    public void Throws_when_scopes_null()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ScopeOrAppRoleRequirement(null!, ["Orders.Read.All"]));
    }

    [Fact]
    public void Throws_when_app_roles_null()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ScopeOrAppRoleRequirement(["Orders.Read"], null!));
    }
}
