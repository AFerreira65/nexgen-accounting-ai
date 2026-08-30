[CmdletBinding()]
param(
    [switch]$NoBrowser
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$webRoot = Join-Path $projectRoot 'src\AccountingAI.Web'
$apiProject = Join-Path $projectRoot 'src\AccountingAI.Api\AccountingAI.Api.csproj'
$webUrl = 'http://127.0.0.1:5173/'
$apiHealthUrl = 'http://127.0.0.1:5080/health/live'

function Test-LocalUrl {
    param([Parameter(Mandatory)][string]$Url)
    try {
        $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 2
        return $response.StatusCode -ge 200 -and $response.StatusCode -lt 500
    }
    catch {
        return $false
    }
}

if (-not (Test-LocalUrl -Url $apiHealthUrl)) {
    $apiArguments = "run --project `"$apiProject`" --configuration Debug --no-launch-profile -- --urls http://127.0.0.1:5080 --environment Development"
    Start-Process -FilePath 'dotnet' -ArgumentList $apiArguments -WorkingDirectory $projectRoot -WindowStyle Hidden | Out-Null
}

if (-not (Test-Path -LiteralPath (Join-Path $webRoot 'node_modules'))) {
    & npm.cmd ci --ignore-scripts --prefix $webRoot
    if ($LASTEXITCODE -ne 0) { throw 'Não foi possível instalar as dependências Web.' }
}

if (-not (Test-LocalUrl -Url $webUrl)) {
    Start-Process -FilePath 'npm.cmd' -ArgumentList 'run dev -- --host 127.0.0.1' -WorkingDirectory $webRoot -WindowStyle Hidden | Out-Null
}

$deadline = [DateTime]::UtcNow.AddSeconds(30)
while (-not (Test-LocalUrl -Url $webUrl)) {
    if ([DateTime]::UtcNow -ge $deadline) { throw "A interface não respondeu em $webUrl no prazo de 30 segundos." }
    Start-Sleep -Milliseconds 250
}

if (-not $NoBrowser) {
    Start-Process $webUrl | Out-Null
}

Write-Host "Accounting AI disponível em $webUrl" -ForegroundColor Green
Write-Host "API local: http://127.0.0.1:5080" -ForegroundColor DarkGray
