using System.Security.Cryptography;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EntraAuth.AspNetCore.Tests;

public sealed class EntraApiAuthenticationTests
{
    private const string TenantId = "11111111-1111-1111-1111-111111111111";
    private const string ClientId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string Audience = "api://aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

    [Fact]
    public void AddEntraApiAuthentication_configures_issuers_audiences_and_test_signing_key()
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "test-key" };
        using var provider = BuildServices(services =>
        {
            services.AddEntraApiAuthentication(Configuration(), options => options.TestSigningKey = key);
        });

        var jwt = provider
            .GetRequiredService<IOptionsSnapshot<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal(
            [$"https://login.microsoftonline.com/{TenantId}/v2.0"],
            jwt.TokenValidationParameters.ValidIssuers);
        Assert.Equal([Audience, ClientId], jwt.TokenValidationParameters.ValidAudiences);
        Assert.True(jwt.TokenValidationParameters.ValidateIssuer);
        Assert.True(jwt.TokenValidationParameters.ValidateAudience);
        Assert.True(jwt.TokenValidationParameters.RequireSignedTokens);
        Assert.Same(key, jwt.TokenValidationParameters.IssuerSigningKey);
        Assert.False(jwt.RequireHttpsMetadata);
        Assert.Equal("oid", jwt.TokenValidationParameters.NameClaimType);
        Assert.Equal("roles", jwt.TokenValidationParameters.RoleClaimType);
    }

    [Fact]
    public async Task AddEntraApiAuthorization_registers_named_policy_and_fallback()
    {
        using var provider = BuildServices(services =>
        {
            services.AddEntraApiAuthorization(Configuration());
        });

        var policies = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var read = await policies.GetPolicyAsync("Orders.Read");
        Assert.NotNull(read);
        Assert.Contains(read.Requirements, r => r is ScopeOrAppRoleRequirement);

        var requirement = read.Requirements.OfType<ScopeOrAppRoleRequirement>().Single();
        Assert.Equal(["Orders.Read"], requirement.Scopes);
        Assert.Equal(["Orders.Read.All"], requirement.AppRoles);

        var options = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
        Assert.NotNull(options.FallbackPolicy);
        Assert.Contains(
            options.FallbackPolicy.Requirements,
            r => r is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public void AddEntraApiAuthentication_throws_when_services_null()
    {
        Assert.Throws<ArgumentNullException>(() =>
            EntraApiServiceCollectionExtensions.AddEntraApiAuthentication(null!, Configuration()));
    }

    [Fact]
    public void AddEntraApiAuthorization_throws_when_configuration_null()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ServiceCollection().AddEntraApiAuthorization(null!));
    }

    private static ServiceProvider BuildServices(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        configure(services);
        return services.BuildServiceProvider();
    }

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EntraApi:Instance"] = "https://login.microsoftonline.com/",
                ["EntraApi:TenantId"] = TenantId,
                ["EntraApi:ClientId"] = ClientId,
                ["EntraApi:Audience"] = Audience,
                ["EntraApi:AllowedTenants:0"] = TenantId,
                ["EntraApi:Policies:Orders.Read:Scopes:0"] = "Orders.Read",
                ["EntraApi:Policies:Orders.Read:AppRoles:0"] = "Orders.Read.All",
            })
            .Build();
}
