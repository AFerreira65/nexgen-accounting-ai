[CmdletBinding()]
param(
    [string]$PublishDirectory
)

# Publica a API (com a PWA embutida em wwwroot) pronta para correr como
# serviço do Windows - ver docs/phase-2/F2-11-AUTHENTICATION.md (F2-11B) e
# docs/OPERACAO-SERVICO-WINDOWS.md para o resto dos passos (instalação do
# serviço, que precisa de administrador e por isso fica fora deste script).

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $projectRoot 'src\AccountingAI.Api\AccountingAI.Api.csproj'
$webProject = Join-Path $projectRoot 'src\AccountingAI.Web'
$webDist = Join-Path $webProject 'dist'
$apiWwwroot = Join-Path $projectRoot 'src\AccountingAI.Api\wwwroot'

if (-not $PublishDirectory) {
    $PublishDirectory = Join-Path $projectRoot 'artifacts\AccountingAI-Service'
}

Write-Host "A construir a PWA (produção)..."
Push-Location $webProject
try {
    npm run build
    if ($LASTEXITCODE -ne 0) { throw 'A compilação da PWA falhou.' }
}
finally {
    Pop-Location
}

Write-Host "A copiar a PWA para $apiWwwroot..."
if (Test-Path $apiWwwroot) {
    Get-ChildItem $apiWwwroot -Force | Where-Object { $_.Name -ne '.gitkeep' } | Remove-Item -Recurse -Force
}
else {
    New-Item -ItemType Directory -Path $apiWwwroot | Out-Null
}
Copy-Item (Join-Path $webDist '*') $apiWwwroot -Recurse -Force

Write-Host "A publicar a API em $PublishDirectory..."
dotnet publish $apiProject -c Release -o $PublishDirectory
if ($LASTEXITCODE -ne 0) { throw 'A publicação da API falhou.' }

Write-Host ""
Write-Host "Publicado em: $PublishDirectory"
Write-Host "Falta instalar o serviço do Windows - precisa de administrador, ver docs/OPERACAO-SERVICO-WINDOWS.md."
