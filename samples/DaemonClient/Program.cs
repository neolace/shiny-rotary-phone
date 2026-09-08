using System.Net.Http.Headers;
using System.Text.Json;

using Microsoft.Identity.Client;

var settings = LoadSettings();
var app = ConfidentialClientApplicationBuilder
    .Create(settings.DaemonClientId)
    .WithTenantId(settings.TenantId)
    .WithClientSecret(settings.DaemonClientSecret)
    .Build();

var scope = $"{settings.Audience}/.default";
var token = await app.AcquireTokenForClient([scope]).ExecuteAsync();

var baseUrl = Environment.GetEnvironmentVariable("API_BASE_URL")
              ?? settings.ApiBaseUrl
              ?? "http://localhost:8080";

using var client = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

Console.WriteLine($"GET {baseUrl}/orders as daemon {settings.DaemonClientId}");
var response = await client.GetAsync("orders");
var body = await response.Content.ReadAsStringAsync();
Console.WriteLine($"{(int)response.StatusCode} {response.ReasonPhrase}");
Console.WriteLine(body);
if (!response.IsSuccessStatusCode)
{
    Environment.ExitCode = 1;
}

static LocalSettings LoadSettings()
{
    var path = FindLocalSettings();
    if (path is null)
    {
        throw new InvalidOperationException(
            "Missing infra/entra.local.json. Run ./scripts/entra-bootstrap.ps1 after az login.");
    }

    var json = File.ReadAllText(path);
    var settings = JsonSerializer.Deserialize<LocalSettings>(json, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
    }) ?? throw new InvalidOperationException($"Could not parse {path}");

    if (string.IsNullOrWhiteSpace(settings.DaemonClientSecret))
    {
        settings = settings with
        {
            DaemonClientSecret = Environment.GetEnvironmentVariable("ENTRA_DAEMON_CLIENT_SECRET"),
        };
    }

    if (string.IsNullOrWhiteSpace(settings.TenantId) ||
        string.IsNullOrWhiteSpace(settings.DaemonClientId) ||
        string.IsNullOrWhiteSpace(settings.DaemonClientSecret) ||
        string.IsNullOrWhiteSpace(settings.Audience))
    {
        throw new InvalidOperationException(
            "entra.local.json is missing tenantId, daemonClientId, daemonClientSecret, or audience.");
    }

    return settings;
}

static string? FindLocalSettings()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        var candidate = Path.Combine(dir.FullName, "infra", "entra.local.json");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        dir = dir.Parent;
    }

    return null;
}

internal sealed record LocalSettings(
    string TenantId,
    string ApiClientId,
    string Audience,
    string DaemonClientId,
    string? DaemonClientSecret,
    string? ApiBaseUrl);
