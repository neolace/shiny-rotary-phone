using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

using Microsoft.IdentityModel.Tokens;

namespace EntraAuth.AspNetCore.Tests;

[Collection("api")]
public sealed class ProtectedEndpointTests
{
    private readonly ApiFixture _fx;

    public ProtectedEndpointTests(ApiFixture fx) => _fx = fx;

    [Fact]
    public async Task Health_allows_anonymous()
    {
        var response = await _fx.Client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Orders_without_token_returns_401_with_www_authenticate()
    {
        var response = await _fx.Client.GetAsync("/orders");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("invalid_token", response.Headers.WwwAuthenticate.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task User_with_read_scope_can_list_orders()
    {
        var token = TestTokens.User(_fx.SigningKey, "Orders.Read");
        var response = await GetOrders(token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task User_with_read_scope_cannot_write_orders()
    {
        var token = TestTokens.User(_fx.SigningKey, "Orders.Read");
        var response = await PostOrders(token);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task App_with_read_role_can_list_orders()
    {
        var token = TestTokens.App(_fx.SigningKey, "Orders.Read.All");
        var response = await GetOrders(token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task App_with_write_role_can_create_orders()
    {
        var token = TestTokens.App(_fx.SigningKey, "Orders.Write.All");
        var response = await PostOrders(token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Expired_token_returns_401()
    {
        var token = TestTokens.User(_fx.SigningKey, "Orders.Read", d =>
        {
            d.IssuedAt = DateTime.UtcNow.AddHours(-3);
            d.NotBefore = DateTime.UtcNow.AddHours(-3);
            d.Expires = DateTime.UtcNow.AddHours(-2);
        });
        var response = await GetOrders(token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_audience_returns_401()
    {
        var token = TestTokens.User(_fx.SigningKey, "Orders.Read", d => d.Audience = "api://someone-else");
        var response = await GetOrders(token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_issuer_returns_401()
    {
        var token = TestTokens.User(_fx.SigningKey, "Orders.Read", d =>
            d.Issuer = "https://login.microsoftonline.com/common/v2.0");
        var response = await GetOrders(token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Foreign_tenant_returns_401()
    {
        var token = TestTokens.Create(_fx.SigningKey, new Dictionary<string, object>
        {
            ["tid"] = ApiFixture.OtherTenantId,
            ["oid"] = "44444444-4444-4444-4444-444444444444",
            ["ver"] = "2.0",
            ["scp"] = "Orders.Read",
        }, d => d.Issuer = $"https://login.microsoftonline.com/{ApiFixture.OtherTenantId}/v2.0");
        var response = await GetOrders(token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unsigned_token_returns_401()
    {
        using var otherRsa = RSA.Create(2048);
        var otherKey = new RsaSecurityKey(otherRsa) { KeyId = "other" };
        var token = TestTokens.User(otherKey, "Orders.Read");
        var response = await GetOrders(token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private Task<HttpResponseMessage> GetOrders(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _fx.Client.SendAsync(request);
    }

    private Task<HttpResponseMessage> PostOrders(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _fx.Client.SendAsync(request);
    }
}
