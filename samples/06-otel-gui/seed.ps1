# Copyright (c) Gatewright contributors.
# SPDX-License-Identifier: MIT

[CmdletBinding()]
param(
    [string]$AdminUrl = $env:ADMIN_URL,
    [string]$Catalog = $env:CATALOG
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($AdminUrl)) {
    $AdminUrl = 'http://localhost:8080'
}
if ([string]::IsNullOrWhiteSpace($Catalog)) {
    $Catalog = Join-Path $PSScriptRoot 'catalog.json'
}

$catalogPath = (Resolve-Path -LiteralPath $Catalog).ProviderPath
$admin = $AdminUrl.TrimEnd('/') + '/admin/v1'
$wait = [System.Diagnostics.Stopwatch]::StartNew()

while ($true) {
    try {
        Invoke-RestMethod -Uri "$admin/environments" -TimeoutSec 5 | Out-Null
        break
    }
    catch {
        $response = $_.Exception.Response
        if (($response -and [int]$response.StatusCode -lt 500) -or $wait.Elapsed.TotalSeconds -ge 60) {
            throw
        }
        Write-Host 'Waiting for the gateway...'
        Start-Sleep -Seconds 2
    }
}

Invoke-RestMethod -Method Post -Uri "$admin/import" -ContentType 'application/json' -InFile $catalogPath -TimeoutSec 30 | Out-Null

# Revision zero publishes the current draft; no separate lookup is needed.
$publication = Invoke-RestMethod -Method Post -Uri "$admin/environments/dev/publications" -ContentType 'application/json' -Body '{"revision":0}' -TimeoutSec 30
Write-Host "Published revision $($publication.revision) to dev. The gateway may take up to 30 seconds to apply it with the sample settings."
