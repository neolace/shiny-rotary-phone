<#
.SYNOPSIS
  Creates (or updates) the Entra API, SPA, and daemon app registrations and
  writes tenant/client IDs into infra/cdk.json.

.DESCRIPTION
  Requires Azure CLI (`az`) signed into a workforce tenant with permission to
  create app registrations. Idempotent on display name.

.EXAMPLE
  ./scripts/entra-bootstrap.ps1
  ./scripts/entra-bootstrap.ps1 -Prefix "orders-prod" -SpaRedirectUri "https://app.example.com"
#>
[CmdletBinding()]
param(
    [string] $Prefix = "entra-auth",
    [string] $SpaRedirectUri = "http://localhost:3000"
)

$ErrorActionPreference = "Stop"

function Assert-AzureCli {
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        throw @"
Azure CLI is not installed. Install it, then sign in:

  winget install -e --id Microsoft.AzureCLI
  az login
  ./scripts/entra-bootstrap.ps1

Portal walkthrough: docs/runbooks/entra-app-registrations.md
"@
    }

    az account show --query tenantId -o tsv 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI is not signed in. Run: az login"
    }
}

function Invoke-Graph {
    param(
        [Parameter(Mandatory)] [ValidateSet("GET", "POST", "PATCH")] [string] $Method,
        [Parameter(Mandatory)] [string] $Uri,
        [object] $Body
    )

    $azArgs = @("rest", "--method", $Method, "--uri", $Uri)
    if ($null -ne $Body) {
        $json = $Body | ConvertTo-Json -Depth 30 -Compress
        $azArgs += @("--headers", "Content-Type=application/json", "--body", $json)
    }

    $raw = & az @azArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Graph $Method $Uri failed."
    }
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return $null
    }
    return $raw | ConvertFrom-Json
}

function Get-ApplicationByName([string] $Name) {
    $filter = [uri]::EscapeDataString("displayName eq '$Name'")
    $page = Invoke-Graph -Method GET -Uri "https://graph.microsoft.com/v1.0/applications?`$filter=$filter"
    return $page.value | Select-Object -First 1
}

function Ensure-Application {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [string[]] $SpaRedirectUris = @()
    )

    $existing = Get-ApplicationByName $Name
    if ($existing) {
        Write-Host "Using existing app '$Name' ($($existing.appId))"
        return $existing
    }

    $body = @{
        displayName     = $Name
        signInAudience  = "AzureADMyOrg"
    }
    if ($SpaRedirectUris.Count -gt 0) {
        $body.spa = @{ redirectUris = @($SpaRedirectUris) }
    }

    Write-Host "Creating app '$Name'"
    return Invoke-Graph -Method POST -Uri "https://graph.microsoft.com/v1.0/applications" -Body $body
}

function Ensure-ServicePrincipal([string] $AppId) {
    $filter = [uri]::EscapeDataString("appId eq '$AppId'")
    $page = Invoke-Graph -Method GET -Uri "https://graph.microsoft.com/v1.0/servicePrincipals?`$filter=$filter"
    $sp = $page.value | Select-Object -First 1
    if ($sp) {
        return $sp
    }
    Write-Host "Creating service principal for $AppId"
    return Invoke-Graph -Method POST -Uri "https://graph.microsoft.com/v1.0/servicePrincipals" -Body @{ appId = $AppId }
}

function Find-IdByValue($items, [string] $value) {
    foreach ($item in @($items)) {
        if ($item.value -eq $value) {
            return $item.id
        }
    }
    return [guid]::NewGuid().ToString()
}

function Set-ApiExposure {
    param(
        [Parameter(Mandatory)] $ApiApp
    )

    $readScopeId = Find-IdByValue $ApiApp.api.oauth2PermissionScopes "Orders.Read"
    $writeScopeId = Find-IdByValue $ApiApp.api.oauth2PermissionScopes "Orders.Write"
    $readRoleId = Find-IdByValue $ApiApp.appRoles "Orders.Read.All"
    $writeRoleId = Find-IdByValue $ApiApp.appRoles "Orders.Write.All"

    $identifierUri = "api://$($ApiApp.appId)"
    $body = @{
        identifierUris = @($identifierUri)
        api            = @{
            requestedAccessTokenVersion = 2
            oauth2PermissionScopes      = @(
                @{
                    id                         = $readScopeId
                    value                      = "Orders.Read"
                    type                       = "Admin"
                    isEnabled                  = $true
                    adminConsentDisplayName    = "Read orders"
                    adminConsentDescription    = "Allows the app to read orders on behalf of the signed-in user."
                    userConsentDisplayName     = "Read your orders"
                    userConsentDescription     = "Allows the app to read your orders."
                }
                @{
                    id                         = $writeScopeId
                    value                      = "Orders.Write"
                    type                       = "Admin"
                    isEnabled                  = $true
                    adminConsentDisplayName    = "Write orders"
                    adminConsentDescription    = "Allows the app to create and change orders on behalf of the signed-in user."
                    userConsentDisplayName     = "Change your orders"
                    userConsentDescription     = "Allows the app to create and change your orders."
                }
            )
        }
        appRoles       = @(
            @{
                id                 = $readRoleId
                value              = "Orders.Read.All"
                displayName        = "Orders.Read.All"
                description        = "Read all orders as an application."
                isEnabled          = $true
                allowedMemberTypes = @("Application")
            }
            @{
                id                 = $writeRoleId
                value              = "Orders.Write.All"
                displayName        = "Orders.Write.All"
                description        = "Write all orders as an application."
                isEnabled          = $true
                allowedMemberTypes = @("Application")
            }
        )
    }

    Invoke-Graph -Method PATCH -Uri "https://graph.microsoft.com/v1.0/applications/$($ApiApp.id)" -Body $body | Out-Null
    return [pscustomobject]@{
        IdentifierUri = $identifierUri
        ReadScopeId   = $readScopeId
        WriteScopeId  = $writeScopeId
        ReadRoleId    = $readRoleId
        WriteRoleId   = $writeRoleId
    }
}

function Set-PreAuthorizedSpa {
    param($ApiApp, [string] $SpaAppId, $Exposure)
    Invoke-Graph -Method PATCH -Uri "https://graph.microsoft.com/v1.0/applications/$($ApiApp.id)" -Body @{
        api = @{
            requestedAccessTokenVersion = 2
            preAuthorizedApplications   = @(
                @{
                    appId                  = $SpaAppId
                    delegatedPermissionIds = @($Exposure.ReadScopeId, $Exposure.WriteScopeId)
                }
            )
        }
    } | Out-Null
}

function Set-DaemonPermissions {
    param($DaemonApp, [string] $ApiAppId, $Exposure)
    Invoke-Graph -Method PATCH -Uri "https://graph.microsoft.com/v1.0/applications/$($DaemonApp.id)" -Body @{
        requiredResourceAccess = @(
            @{
                resourceAppId  = $ApiAppId
                resourceAccess = @(
                    @{ id = $Exposure.ReadRoleId; type = "Role" }
                    @{ id = $Exposure.WriteRoleId; type = "Role" }
                )
            }
        )
    } | Out-Null
}

function Set-SpaPermissions {
    param($SpaApp, [string] $ApiAppId, $Exposure)
    Invoke-Graph -Method PATCH -Uri "https://graph.microsoft.com/v1.0/applications/$($SpaApp.id)" -Body @{
        requiredResourceAccess = @(
            @{
                resourceAppId  = $ApiAppId
                resourceAccess = @(
                    @{ id = $Exposure.ReadScopeId; type = "Scope" }
                    @{ id = $Exposure.WriteScopeId; type = "Scope" }
                )
            }
        )
    } | Out-Null
}

function Set-AssignmentRequired([string] $ServicePrincipalId) {
    Invoke-Graph -Method PATCH -Uri "https://graph.microsoft.com/v1.0/servicePrincipals/$ServicePrincipalId" -Body @{
        appRoleAssignmentRequired = $true
    } | Out-Null
}

function Ensure-AppRoleAssignment {
    param([string] $ResourceSpId, [string] $PrincipalSpId, [string] $AppRoleId)
    $existing = Invoke-Graph -Method GET -Uri "https://graph.microsoft.com/v1.0/servicePrincipals/$ResourceSpId/appRoleAssignedTo"
    foreach ($row in @($existing.value)) {
        if ($row.principalId -eq $PrincipalSpId -and $row.appRoleId -eq $AppRoleId) {
            return
        }
    }
    Invoke-Graph -Method POST -Uri "https://graph.microsoft.com/v1.0/servicePrincipals/$ResourceSpId/appRoleAssignedTo" -Body @{
        principalId = $PrincipalSpId
        resourceId  = $ResourceSpId
        appRoleId   = $AppRoleId
    } | Out-Null
}

function New-DaemonSecretIfMissing([string] $DaemonAppId) {
    $list = & az ad app credential list --id $DaemonAppId | ConvertFrom-Json
    if (@($list).Count -gt 0) {
        Write-Host "Daemon already has a credential; not rotating it. Re-run with az ad app credential reset if you need a new secret."
        return $null
    }
    Write-Host "Creating daemon client secret (shown once in infra/entra.local.json)."
    $created = & az ad app credential reset --id $DaemonAppId --display-name "bootstrap" --years 1 | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create daemon client secret."
    }
    return $created.password
}

function Update-CdkContext {
    param([string] $TenantId, [string] $ApiClientId, [string] $Audience, [string] $SpaRedirectUri)
    $infraDir = (Join-Path $PSScriptRoot "..\infra" | Resolve-Path).Path
    $patchPath = Join-Path $infraDir "entra.patch.json"
    @{
        tenantId       = $TenantId
        apiClientId    = $ApiClientId
        audience       = $Audience
        allowedOrigins = @($SpaRedirectUri.TrimEnd("/"))
    } | ConvertTo-Json | Set-Content -Path $patchPath -Encoding utf8
    node --input-type=commonjs -e "const fs=require('fs');const path=require('path');const dir=process.argv[1];const patch=JSON.parse(fs.readFileSync(path.join(dir,'entra.patch.json'),'utf8'));const file=path.join(dir,'cdk.json');const cdk=JSON.parse(fs.readFileSync(file,'utf8'));cdk.context=cdk.context||{};cdk.context.entra=patch;fs.writeFileSync(file, JSON.stringify(cdk,null,2)+'\n');fs.unlinkSync(path.join(dir,'entra.patch.json'));" $infraDir
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to patch infra/cdk.json"
    }
    Write-Host "Updated infra/cdk.json context.entra"
}

Assert-AzureCli

$tenantId = (az account show --query tenantId -o tsv).Trim()
Write-Host "Tenant $tenantId"

$apiApp = Ensure-Application -Name "$Prefix-api"
$spaApp = Ensure-Application -Name "$Prefix-spa" -SpaRedirectUris @($SpaRedirectUri)
if ($spaApp.id) {
    Invoke-Graph -Method PATCH -Uri "https://graph.microsoft.com/v1.0/applications/$($spaApp.id)" -Body @{
        spa = @{ redirectUris = @($SpaRedirectUri) }
    } | Out-Null
}
$daemonApp = Ensure-Application -Name "$Prefix-daemon"

$exposure = Set-ApiExposure -ApiApp $apiApp
# Reload API after PATCH so ids are current
$apiApp = Get-ApplicationByName "$Prefix-api"
$exposure = Set-ApiExposure -ApiApp $apiApp

Set-PreAuthorizedSpa -ApiApp $apiApp -SpaAppId $spaApp.appId -Exposure $exposure
Set-SpaPermissions -SpaApp $spaApp -ApiAppId $apiApp.appId -Exposure $exposure
Set-DaemonPermissions -DaemonApp $daemonApp -ApiAppId $apiApp.appId -Exposure $exposure

$apiSp = Ensure-ServicePrincipal $apiApp.appId
$spaSp = Ensure-ServicePrincipal $spaApp.appId
$daemonSp = Ensure-ServicePrincipal $daemonApp.appId
Set-AssignmentRequired $apiSp.id
Ensure-AppRoleAssignment -ResourceSpId $apiSp.id -PrincipalSpId $daemonSp.id -AppRoleId $exposure.ReadRoleId
Ensure-AppRoleAssignment -ResourceSpId $apiSp.id -PrincipalSpId $daemonSp.id -AppRoleId $exposure.WriteRoleId

$secret = New-DaemonSecretIfMissing $daemonApp.appId

Update-CdkContext -TenantId $tenantId -ApiClientId $apiApp.appId -Audience $exposure.IdentifierUri -SpaRedirectUri $SpaRedirectUri

$local = @{
    tenantId           = $tenantId
    apiClientId        = $apiApp.appId
    audience           = $exposure.IdentifierUri
    issuer             = "https://login.microsoftonline.com/$tenantId/v2.0"
    spaClientId        = $spaApp.appId
    daemonClientId     = $daemonApp.appId
    spaRedirectUri     = $SpaRedirectUri
}
if ($secret) {
    $local.daemonClientSecret = $secret
}

$localPath = Join-Path $PSScriptRoot "..\infra\entra.local.json" | Resolve-Path -ErrorAction SilentlyContinue
if (-not $localPath) {
    $localPath = Join-Path (Split-Path $PSScriptRoot -Parent) "infra\entra.local.json"
}
# Preserve an existing secret if we did not rotate
if ((Test-Path $localPath) -and -not $secret) {
    $previous = Get-Content $localPath -Raw | ConvertFrom-Json
    if ($previous.daemonClientSecret) {
        $local.daemonClientSecret = $previous.daemonClientSecret
    }
}

$local | ConvertTo-Json -Depth 10 | Set-Content -Path $localPath -Encoding utf8

Write-Host ""
Write-Host "Done."
Write-Host "  API client ID : $($apiApp.appId)"
Write-Host "  Audience      : $($exposure.IdentifierUri)"
Write-Host "  SPA client ID : $($spaApp.appId)"
Write-Host "  Daemon ID     : $($daemonApp.appId)"
Write-Host "  Local file    : infra/entra.local.json (gitignored)"
Write-Host ""
Write-Host "Grant admin consent in Entra if the portal still shows it pending:"
Write-Host "  API permissions on $Prefix-spa and $Prefix-daemon -> Grant admin consent"
Write-Host ""
Write-Host "Then deploy: cd infra; npx cdk deploy"
