using System.Security.Cryptography;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace EntraAuth.AspNetCore.Tests;

public sealed class ApiFixture : IAsyncLifetime
{
    public const string TenantId = "11111111-1111-1111-1111-111111111111";
    public const string OtherTenantId = "99999999-9999-9999-9999-999999999999";
    public const string ClientId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    public const string Audience = "api://aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    public const string Issuer = "https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111/v2.0";

    public WebApplication App { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public RsaSecurityKey SigningKey { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var rsa = RSA.Create(2048);
        SigningKey = new RsaSecurityKey(rsa) { KeyId = "test-key" };

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EntraApi:Instance"] = "https://login.microsoftonline.com/",
            ["EntraApi:TenantId"] = TenantId,
            ["EntraApi:ClientId"] = ClientId,
            ["EntraApi:Audience"] = Audience,
            ["EntraApi:AllowedTenants:0"] = TenantId,
            ["EntraApi:Policies:Orders.Read:Scopes:0"] = "Orders.Read",
            ["EntraApi:Policies:Orders.Read:AppRoles:0"] = "Orders.Read.All",
            ["EntraApi:Policies:Orders.Write:Scopes:0"] = "Orders.Write",
            ["EntraApi:Policies:Orders.Write:AppRoles:0"] = "Orders.Write.All",
        });

        builder.Services.AddEntraApiAuthentication(builder.Configuration, options =>
        {
            options.TestSigningKey = SigningKey;
        });
        builder.Services.AddEntraApiAuthorization(builder.Configuration);

        App = builder.Build();
        App.UseAuthentication();
        App.UseAuthorization();
        App.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
        App.MapGet("/orders", () => Results.Ok(new { items = Array.Empty<string>() }))
            .RequireAuthorization("Orders.Read");
        App.MapPost("/orders", () => Results.StatusCode(StatusCodes.Status201Created))
            .RequireAuthorization("Orders.Write");

        await App.StartAsync();
        Client = App.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await App.DisposeAsync();
        SigningKey.Rsa?.Dispose();
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;
