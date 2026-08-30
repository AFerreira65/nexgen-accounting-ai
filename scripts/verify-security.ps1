[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $projectRoot 'Accounting AI.slnx'
$webProject = Join-Path $projectRoot 'src\AccountingAI.Web'
$toolVersion = '8.30.1'
$toolChecksum = 'd29144deff3a68aa93ced33dddf84b7fdc26070add4aa0f4513094c8332afc4e'
$toolDirectory = Join-Path $projectRoot ".tools\gitleaks\$toolVersion"
$toolPath = Join-Path $toolDirectory 'gitleaks.exe'

Write-Host 'A verificar vulnerabilidades NuGet...' -ForegroundColor Cyan
$packageOutput = dotnet list $solution package --vulnerable --include-transitive --format json --no-restore
if ($LASTEXITCODE -ne 0) { throw 'A auditoria NuGet falhou.' }
$packageReport = $packageOutput | ConvertFrom-Json
$vulnerabilityCount = 0
foreach ($project in @($packageReport.projects)) {
    foreach ($framework in @($project.frameworks)) {
        foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
            if ($null -ne $package -and $null -ne $package.vulnerabilities) {
                $vulnerabilityCount += @($package.vulnerabilities).Count
            }
        }
    }
}
if ($vulnerabilityCount -gt 0) {
    throw "Foram encontradas $vulnerabilityCount vulnerabilidades NuGet conhecidas."
}

Write-Host 'A verificar vulnerabilidades npm de severidade alta ou crítica...' -ForegroundColor Cyan
Push-Location $webProject
try {
    npm audit --audit-level=high
    if ($LASTEXITCODE -ne 0) { throw 'A auditoria npm encontrou vulnerabilidades altas ou críticas.' }
}
finally {
    Pop-Location
}

if (-not (Test-Path -LiteralPath $toolPath)) {
    New-Item -ItemType Directory -Path $toolDirectory -Force | Out-Null
    $archivePath = Join-Path $toolDirectory 'gitleaks.zip'
    $downloadUri = "https://github.com/gitleaks/gitleaks/releases/download/v$toolVersion/gitleaks_${toolVersion}_windows_x64.zip"
    Invoke-WebRequest -Uri $downloadUri -OutFile $archivePath -UseBasicParsing
    $actualChecksum = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualChecksum -ne $toolChecksum) {
        Remove-Item -LiteralPath $archivePath -Force
        throw 'O checksum do Gitleaks não corresponde à versão aprovada.'
    }
    Expand-Archive -LiteralPath $archivePath -DestinationPath $toolDirectory -Force
    Remove-Item -LiteralPath $archivePath -Force
}

Write-Host 'A procurar segredos em ficheiros versionáveis...' -ForegroundColor Cyan
& $toolPath dir $projectRoot --config (Join-Path $projectRoot '.gitleaks.toml') --redact --no-banner --exit-code 1
if ($LASTEXITCODE -ne 0) { throw 'A deteção de segredos encontrou ocorrências que exigem revisão.' }

Write-Host 'Gates de segurança concluídos sem ocorrências.' -ForegroundColor Green
