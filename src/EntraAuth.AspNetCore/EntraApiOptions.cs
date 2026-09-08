namespace EntraAuth.AspNetCore;

public sealed class EntraApiOptions
{
    public const string SectionName = "EntraApi";

    public string Instance { get; set; } = "https://login.microsoftonline.com/";
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int ClockSkewSeconds { get; set; } = 120;
    public List<string> AllowedTenants { get; set; } = [];
    public Dictionary<string, EntraApiPolicyOptions> Policies { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> ResolvedAudiences()
    {
        var audiences = new List<string>();
        if (!string.IsNullOrWhiteSpace(Audience))
        {
            audiences.Add(Audience);
        }

        if (!string.IsNullOrWhiteSpace(ClientId) &&
            !audiences.Contains(ClientId, StringComparer.OrdinalIgnoreCase))
        {
            audiences.Add(ClientId);
        }

        return audiences;
    }

    public IReadOnlyList<string> ResolvedTenants()
    {
        if (AllowedTenants.Count > 0)
        {
            return AllowedTenants;
        }

        return string.IsNullOrWhiteSpace(TenantId) ? [] : [TenantId];
    }

    public IReadOnlyList<string> ResolvedIssuers()
    {
        var instance = Instance.TrimEnd('/');
        return ResolvedTenants()
            .Select(tid => $"{instance}/{tid}/v2.0")
            .ToArray();
    }
}

public sealed class EntraApiPolicyOptions
{
    public List<string> Scopes { get; set; } = [];
    public List<string> AppRoles { get; set; } = [];
}
