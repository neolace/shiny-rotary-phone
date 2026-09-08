using Microsoft.IdentityModel.Tokens;

namespace EntraAuth.AspNetCore;

public sealed class EntraApiAuthenticationOptions
{
    /// <summary>
    /// When set, JWT validation uses this key and skips Entra JWKS metadata.
    /// Intended for tests only.
    /// </summary>
    public SecurityKey? TestSigningKey { get; set; }
}
