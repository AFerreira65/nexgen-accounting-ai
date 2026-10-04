[CmdletBinding()]
param(
    [string]$ConnectionString
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $projectRoot 'src\AccountingAI.Api\AccountingAI.Api.csproj'
$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
$previousConnectionString = ${env:ConnectionStrings__Accounting}

try {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'

    if (-not [string]::IsNullOrWhiteSpace($ConnectionString)) {
        ${env:ConnectionStrings__Accounting} = $ConnectionString
    }

    dotnet run --project $apiProject -c Release --no-build --no-restore -- --migrate-only
    if ($LASTEXITCODE -ne 0) { throw 'A migração da base de dados falhou.' }
}
finally {
    $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
    ${env:ConnectionStrings__Accounting} = $previousConnectionString
}
