# Runbook: operate the API Gateway + ECS stack

CDK app: `infra/`. Stack id: `EntraAuth`.

## What is deployed

- Amazon API Gateway **HTTP API** (public default endpoint)
- JWT authorizer: Entra v2 issuer + audience (signature via JWKS)
- VPC link → **internal** Application Load Balancer → ECS Fargate (SampleApi)
- NAT Gateway so tasks can fetch Entra JWKS (`login.microsoftonline.com`)
- HTTP API access logs in CloudWatch Logs

TLS terminates on API Gateway. The ALB listener is HTTP inside the VPC.

HTTP API integration timeout is **30 seconds** (hard). Do not put long jobs on this path.

## First deploy

Prerequisites: AWS credentials, CDK bootstrap, Docker (image asset build). To synthesize the template without Docker:

```powershell
npx cdk synth -c sampleImage=public.ecr.aws/docker/library/httpd:2.4
```

Deploy always uses `samples/SampleApi/Dockerfile` unless you pass `sampleImage`.

```powershell
cd infra
npm install
# edit cdk.json context.entra with real tenant/client/audience
npx cdk bootstrap
npx cdk diff
npx cdk deploy
```

Output `ApiUrl` is the base URL. `/health` is unauthenticated. Everything else requires a Bearer token.

Replace the placeholder GUIDs in `cdk.json` before any environment that Entra can issue tokens for. The placeholders exist so `cdk synth` can run without a tenant.

## Dual-gate behavior

| Layer                   | Checks                                                         | Does not check                                            |
| ----------------------- | -------------------------------------------------------------- | --------------------------------------------------------- |
| HTTP API JWT authorizer | `iss`, `aud`/`client_id`, signature (RS256), `exp`/`nbf`/`iat` | App roles, tenant allow-list beyond issuer, custom claims |
| `EntraAuth.AspNetCore`  | Same, plus `tid` allow-list, scopes **or** app roles           | Row-level / data authorization                            |

Do **not** attach `authorizationScopes` on API Gateway routes. Daemon tokens have `roles` and no `scp`; the gateway would 403 them.

JWKS public keys are cached by API Gateway for **two hours**. Coordinate Entra signing-key rollover with that cache.

## Health and scaling

- ALB target-group health check: `GET /health` on the tasks (not through API Gateway).
- Desired count is 2. Use ECS service auto scaling if load requires it.
- Circuit breaker with rollback is on for failed deployments.

## Logs

| Signal             | Where                                                                                     |
| ------------------ | ----------------------------------------------------------------------------------------- |
| HTTP API access    | Log group `HttpApiAccessLogs` (logical id; name is generated)                             |
| ECS container      | `/ecs/...` log group created by the Fargate pattern                                       |
| Authn/authz in-app | Application logs. Tokens must never appear. `tid`, `oid`, `azp`, `scp`/`roles` are enough |

Useful access-log filters: `status = 401`, `status = 403`, `authorizerError`.

## CORS

Origins come from `context.entra.allowedOrigins`. API Gateway handles OPTIONS. Allowed headers: `Authorization`, `Content-Type`. Do not use `*`.

## WAF

HTTP APIs do not support AWS WAF on the API itself. If you need WAF or edge caching:

1. Create a CloudFront distribution in front of the HTTP API (Regional).
2. Attach AWS WAF to that distribution.
3. Disable the default `execute-api` endpoint or restrict it after the custom domain is live.

## Secrets and IAM

The sample API is a **resource**. It does not store an Entra client secret.

- Task **execution** role: pull image, write logs.
- Task **role**: empty for the sample. Grant AWS API access here when the API needs S3/SQS/etc. Do not put AWS access keys in Entra tokens.

If a future endpoint uses On-Behalf-Of or client credentials to call Graph, store the certificate or secret in Secrets Manager and grant the task role `secretsmanager:GetSecretValue` on that secret only.

## Change recipes

| Change                    | Action                                                                         |
| ------------------------- | ------------------------------------------------------------------------------ |
| New Entra tenant/audience | Update `cdk.json` `entra`, `cdk deploy`. JWT authorizer is replaced in place.  |
| New API permission        | Entra registration + appsettings/policy + container deploy. Gateway unchanged. |
| New container             | `cdk deploy` rebuilds the image asset.                                         |
| Scale                     | Change `desiredCount` or add auto scaling in `entra-auth-stack.ts`.            |

## Destroy

```powershell
cd infra
npx cdk destroy
```

NAT Gateway and ALB stop billing after the stack is deleted. Confirm the ECS service reaches 0 tasks.

## Local API without AWS

```powershell
dotnet run --project samples/SampleApi
```

Set `EntraApi__TenantId`, `EntraApi__ClientId`, and `EntraApi__Audience` to the real registration. The process will call Entra JWKS. Tests do not; they use a local RSA key.
