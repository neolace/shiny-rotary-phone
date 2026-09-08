# Entra API auth layer on AWS

ASP.NET Core library that validates Microsoft Entra ID access tokens, plus a sample API behind **Amazon API Gateway HTTP API** (JWT authorizer) on **ECS Fargate**. Infrastructure is **CDK only**.

Clients obtain tokens from Entra. API Gateway checks issuer, audience, and signature. The .NET API is the source of truth for tenant allow-list, scopes (`scp`), and app roles (`roles`).

## Layout

| Path                                       | Role                                                                          |
| ------------------------------------------ | ----------------------------------------------------------------------------- |
| `src/EntraAuth.AspNetCore`                 | Reusable auth/authz layer                                                     |
| `tests/EntraAuth.AspNetCore.Tests`         | JWT 401/403/200 matrix (no live tenant)                                       |
| `samples/SampleApi`                        | Minimal API using the layer                                                   |
| `infra`                                    | CDK TypeScript: HTTP API + JWT authorizer + VPC link + internal ALB + Fargate |
| `docs/runbooks/entra-app-registrations.md` | Entra API, SPA, and daemon registrations                                      |
| `docs/runbooks/operate-api-gateway.md`     | Deploy, logs, CORS, WAF follow-up                                             |

## Format and lint

Format on save is on in `.vscode/settings.json`. Commits run the same formatters via Husky + lint-staged.

```powershell
npm run format
npm run lint
```

`Directory.Build.props` turns on .NET analyzers and treats warnings as errors. CDK uses ESLint (`eslint-plugin-awscdk`) plus Prettier. CI still checks with `--verify-no-changes` / `prettier --check`.

## Quick start (library)

```powershell
dotnet test tests/EntraAuth.AspNetCore.Tests
```

In an API:

```csharp
builder.Services.AddEntraApiAuthentication(builder.Configuration);
builder.Services.AddEntraApiAuthorization(builder.Configuration);

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/orders", Handler).RequireAuthorization("Orders.Read");
```

Configuration section `EntraApi` is documented in `samples/SampleApi/appsettings.json`.

## Deploy

This environment could not sign in to Entra or AWS (no `az`, no AWS credentials, Docker daemon stopped). On a workstation that has those:

```powershell
az login
./scripts/entra-bootstrap.ps1          # writes infra/cdk.json + gitignored entra.local.json
cd infra
npx cdk bootstrap
npx cdk deploy                         # requires Docker
```

Manual portal steps: [Entra runbook](docs/runbooks/entra-app-registrations.md). Day-2 AWS: [operate runbook](docs/runbooks/operate-api-gateway.md).

Call the API as a daemon (after bootstrap + a running API):

```powershell
dotnet run --project samples/DaemonClient
$env:API_BASE_URL = "https://xxxx.execute-api.region.amazonaws.com"
dotnet run --project samples/DaemonClient
```

Do not put `authorizationScopes` on API Gateway routes: daemon tokens have no `scp`.
