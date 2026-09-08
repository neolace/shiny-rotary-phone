namespace EntraAuth.AspNetCore.Tests;

public sealed class EntraApiOptionsTests
{
    [Fact]
    public void ResolvedAudiences_includes_audience_and_distinct_client_id()
    {
        var options = new EntraApiOptions
        {
            Audience = "api://aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            ClientId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
        };

        Assert.Equal(
            ["api://aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"],
            options.ResolvedAudiences());
    }

    [Fact]
    public void ResolvedAudiences_does_not_duplicate_when_audience_matches_client_id()
    {
        var options = new EntraApiOptions
        {
            Audience = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            ClientId = "AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA",
        };

        Assert.Equal(["aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"], options.ResolvedAudiences());
    }

    [Fact]
    public void ResolvedAudiences_uses_client_id_when_audience_blank()
    {
        var options = new EntraApiOptions { ClientId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" };

        Assert.Equal(["aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"], options.ResolvedAudiences());
    }

    [Fact]
    public void ResolvedAudiences_empty_when_neither_set()
    {
        Assert.Empty(new EntraApiOptions().ResolvedAudiences());
    }

    [Fact]
    public void ResolvedTenants_prefers_allow_list_over_tenant_id()
    {
        var options = new EntraApiOptions
        {
            TenantId = "11111111-1111-1111-1111-111111111111",
            AllowedTenants = ["22222222-2222-2222-2222-222222222222"],
        };

        Assert.Equal(["22222222-2222-2222-2222-222222222222"], options.ResolvedTenants());
    }

    [Fact]
    public void ResolvedTenants_falls_back_to_tenant_id()
    {
        var options = new EntraApiOptions { TenantId = "11111111-1111-1111-1111-111111111111" };

        Assert.Equal(["11111111-1111-1111-1111-111111111111"], options.ResolvedTenants());
    }

    [Fact]
    public void ResolvedTenants_empty_when_unset()
    {
        Assert.Empty(new EntraApiOptions().ResolvedTenants());
    }

    [Fact]
    public void ResolvedIssuers_builds_v2_issuer_and_trims_instance_slash()
    {
        var options = new EntraApiOptions
        {
            Instance = "https://login.microsoftonline.com/",
            TenantId = "11111111-1111-1111-1111-111111111111",
        };

        Assert.Equal(
            ["https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111/v2.0"],
            options.ResolvedIssuers());
    }

    [Fact]
    public void Policies_are_case_insensitive()
    {
        var options = new EntraApiOptions();
        options.Policies["Orders.Read"] = new EntraApiPolicyOptions { Scopes = ["Orders.Read"] };

        Assert.True(options.Policies.ContainsKey("orders.read"));
        Assert.Equal(["Orders.Read"], options.Policies["ORDERS.READ"].Scopes);
    }
}
