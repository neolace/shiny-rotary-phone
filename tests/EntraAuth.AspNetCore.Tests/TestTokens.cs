using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EntraAuth.AspNetCore.Tests;

internal static class TestTokens
{
    public static string User(RsaSecurityKey key, string scope, Action<SecurityTokenDescriptor>? configure = null)
    {
        return Create(key, new Dictionary<string, object>
        {
            ["tid"] = ApiFixture.TenantId,
            ["oid"] = "22222222-2222-2222-2222-222222222222",
            ["ver"] = "2.0",
            ["scp"] = scope,
        }, configure);
    }

    public static string App(RsaSecurityKey key, string role, Action<SecurityTokenDescriptor>? configure = null)
    {
        return Create(key, new Dictionary<string, object>
        {
            ["tid"] = ApiFixture.TenantId,
            ["oid"] = "33333333-3333-3333-3333-333333333333",
            ["ver"] = "2.0",
            ["idtyp"] = "app",
            ["roles"] = new[] { role },
        }, configure);
    }

    public static string Create(
        RsaSecurityKey key,
        IDictionary<string, object> claims,
        Action<SecurityTokenDescriptor>? configure = null)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = ApiFixture.Issuer,
            Audience = ApiFixture.Audience,
            IssuedAt = DateTime.UtcNow,
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(30),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
            Claims = new Dictionary<string, object>(claims),
        };
        configure?.Invoke(descriptor);
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
