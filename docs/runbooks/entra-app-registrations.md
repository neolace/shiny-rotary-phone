# Runbook: Microsoft Entra app registrations

This API is an Entra **resource**. Clients get access tokens from Entra and call Amazon API Gateway with `Authorization: Bearer`. Do not register one app that is both the API and every client.

Replace every `REPLACE` value before production.

## Automated path (preferred)

Requires Azure CLI, signed in with permission to create app registrations.

```powershell
az login
./scripts/entra-bootstrap.ps1
# optional: ./scripts/entra-bootstrap.ps1 -Prefix "orders-prod" -SpaRedirectUri "https://app.example.com"
```

The script is idempotent on display name (`entra-auth-api`, `entra-auth-spa`, `entra-auth-daemon`). It:

- Sets Application ID URI `api://{apiClientId}` and `requestedAccessTokenVersion: 2`
- Publishes `Orders.Read` / `Orders.Write` scopes and `Orders.Read.All` / `Orders.Write.All` app roles
- Pre-authorizes the SPA, grants the daemon application permissions, assignment-required on the API enterprise app
- Writes `infra/cdk.json` `context.entra` and gitignored `infra/entra.local.json` (daemon secret only if none existed)

Then grant **admin consent** on the SPA and daemon API permissions if the portal still shows it pending.

Portal: [Microsoft Entra admin center](https://entra.microsoft.com) → **Identity** → **Applications** → **App registrations**. The rest of this runbook is the manual equivalent.

## Values you will copy into CDK

After the API registration exists, put these in `infra/cdk.json` under `context.entra`:

| Field            | Where it comes from                                                        |
| ---------------- | -------------------------------------------------------------------------- |
| `tenantId`       | Directory (tenant) ID on the API registration overview                     |
| `apiClientId`    | Application (client) ID of the **API** registration                        |
| `audience`       | Application ID URI from **Expose an API** (example: `api://{apiClientId}`) |
| `allowedOrigins` | SPA origins that may call the HTTP API (CORS)                              |

Issuer the JWT authorizer uses:

```
https://login.microsoftonline.com/{tenantId}/v2.0
```

JWKS:

```
https://login.microsoftonline.com/{tenantId}/discovery/v2.0/keys
```

## 1. Register the API (resource)

1. **New registration**
   - Name: `entra-auth-api` (or your product name).
   - Supported account types: **Accounts in this organizational directory only (single tenant)**.
   - Redirect URI: none.
2. Copy **Application (client) ID** and **Directory (tenant) ID**.
3. **Expose an API**
   - Set Application ID URI to `api://{apiClientId}` (or a verified domain URI such as `https://api.example.com`).
   - Add delegated scopes (user tokens, `scp` claim):

     | Scope name     | Who can consent | Admin consent display name |
     | -------------- | --------------- | -------------------------- |
     | `Orders.Read`  | Admins only     | Read orders                |
     | `Orders.Write` | Admins only     | Create and change orders   |

   Full scope strings become `api://{apiClientId}/Orders.Read` and `api://{apiClientId}/Orders.Write`.

4. **App roles** (daemon tokens, `roles` claim). Allowed member types: **Applications**.

   | Display name / Value | Description                        |
   | -------------------- | ---------------------------------- |
   | `Orders.Read.All`    | Read all orders as an application  |
   | `Orders.Write.All`   | Write all orders as an application |

5. **Manifest**
   - Set `"accessTokenAcceptedVersion": 2`.
   - Save. v2 tokens use issuer `https://login.microsoftonline.com/{tid}/v2.0`, which matches API Gateway HTTP API JWT authorizers.
6. Open **Managed application in local directory** (enterprise app) → **Properties**
   - **Assignment required?** = **Yes**.
   - Save. Unassigned daemons then fail at token issuance (`AADSTS501051`) instead of reaching the API.
7. Do **not** add `groups` as an optional claim for authorization. Use scopes and app roles. Group overage will break authorization.

## 2. Register a user client (SPA)

Use this for a browser app that signs users in.

1. **New registration**, single tenant.
2. **Authentication** → platform **Single-page application**.
   - Redirect URI: `http://localhost:3000` (dev) and the production HTTPS origin.
   - Implicit grant: **off** (auth code + PKCE only).
3. **API permissions** → **My APIs** → the API registration → delegated:
   - `Orders.Read`
   - `Orders.Write` (only if the SPA must write)
4. Grant **admin consent**.
5. On the **API** registration → **Expose an API** → **Authorized client applications**, add this SPA’s client ID for those scopes so users are not prompted.

Token request (MSAL):

- Authority: `https://login.microsoftonline.com/{tenantId}`
- Scopes: `api://{apiClientId}/Orders.Read`
- Use the **access token**, never the ID token, on `Authorization`.

## 3. Register a daemon client (service)

Use this for batch jobs, other APIs, and partners running unattended.

1. **New registration**, single tenant. No redirect URI.
2. **Certificates & secrets**
   - Production: upload a certificate (preferred) or configure a federated credential.
   - Avoid long-lived client secrets. If you must use a secret, store it in AWS Secrets Manager and rotate it.
3. **API permissions** → **My APIs** → the API registration → **Application permissions**:
   - `Orders.Read.All` and/or `Orders.Write.All`
4. Grant **admin consent**.
5. Enterprise app of the **API**: assign this daemon (or a service principal group) so assignment-required succeeds.

Token request (client credentials):

```
POST https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token
Content-Type: application/x-www-form-urlencoded

client_id={daemonClientId}
&client_secret={secret}
&grant_type=client_credentials
&scope=api://{apiClientId}/.default
```

The access token `aud` is the API client ID or Application ID URI. `roles` contains the app roles. There is no `scp` claim.

## 4. Wire CDK

Edit `infra/cdk.json`:

```json
"entra": {
  "tenantId": "YOUR-TENANT-GUID",
  "apiClientId": "YOUR-API-CLIENT-GUID",
  "audience": "api://YOUR-API-CLIENT-GUID",
  "allowedOrigins": ["https://app.example.com"]
}
```

The HTTP API JWT authorizer accepts both `audience` and `apiClientId` because Entra v2 tokens often put the GUID in `aud`. The .NET API does the same.

Do not set API Gateway **authorization scopes** on routes. App tokens have no `scp`; the gateway would 403 daemons. Scopes and roles are enforced in `EntraAuth.AspNetCore`.

## 5. Prove a token

1. Decode the access token at [jwt.ms](https://jwt.ms) (paste only test tokens).
2. Confirm:

   | Claim   | User token                                     | App token                              |
   | ------- | ---------------------------------------------- | -------------------------------------- |
   | `iss`   | `https://login.microsoftonline.com/{tid}/v2.0` | same                                   |
   | `aud`   | API client ID or Application ID URI            | same                                   |
   | `tid`   | your tenant                                    | your tenant                            |
   | `ver`   | `2.0`                                          | `2.0`                                  |
   | `scp`   | `Orders.Read` and/or `Orders.Write`            | absent                                 |
   | `roles` | optional user app roles                        | `Orders.Read.All` / `Orders.Write.All` |

3. Call:

```bash
curl -i https://{api-id}.execute-api.{region}.amazonaws.com/health
curl -i https://{api-id}.execute-api.{region}.amazonaws.com/orders \
  -H "Authorization: Bearer {access_token}"
```

Expect `200` on `/health` with no token. Expect `401` on `/orders` without a token, `403` with a valid token that lacks the policy, `200` with `Orders.Read` or `Orders.Read.All`.

## 6. Operational checks

| Symptom                              | Likely cause                                                                                                                                                                |
| ------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| API Gateway `401` immediately        | Missing/malformed `Authorization` header, wrong `iss` or `aud`, expired token, or v1 issuer (`https://sts.windows.net/...`) because `accessTokenAcceptedVersion` is not `2` |
| API Gateway `403` with a valid token | A route-level `authorizationScopes` check failed. Remove gateway scopes; keep authz in the API                                                                              |
| API `401` after gateway `200`        | .NET audience/tenant allow-list does not match the token (`EntraApi__Audience`, `EntraApi__AllowedTenants__0`)                                                              |
| API `403`                            | Token valid but policy failed (need `scp` or `roles`)                                                                                                                       |
| Daemon cannot get a token            | Missing app role assignment, assignment required, or admin consent not granted                                                                                              |
| JWKS / metadata failures from ECS    | Tasks have no egress to `login.microsoftonline.com` (NAT Gateway missing)                                                                                                   |

Do not branch application code on `AADSTS*` strings; Microsoft does not treat them as a stable contract.

## 7. Adding a new permission

1. Add a scope and/or app role on the API registration.
2. Grant it to the client registration; admin-consent.
3. Add a matching policy in `EntraApi:Policies` (sample: `appsettings.json`, ECS environment, or a future config store).
4. Decorate the endpoint with `.RequireAuthorization("PolicyName")`.
5. Redeploy the API container. No JWT authorizer change unless issuer or audience changed.
