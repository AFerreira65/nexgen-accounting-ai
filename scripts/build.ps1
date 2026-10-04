[CmdletBinding()]
param(
    [switch]$Publish,
    [switch]$SyntheticOnly,
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $projectRoot 'NexGen Accounting AI.slnx'
$examples = Join-Path $projectRoot 'SAFTs Exemplo'
$smokeProject = Join-Path $projectRoot 'tests\NexGenAccountingAI.Smoke\NexGenAccountingAI.Smoke.csproj'
$uiSmokeProject = Join-Path $projectRoot 'tests\NexGenAccountingAI.WinForms.Smoke\NexGenAccountingAI.WinForms.Smoke.csproj'
$appProject = Join-Path $projectRoot 'src\NexGenAccountingAI.WinForms\NexGenAccountingAI.WinForms.csproj'
$webProject = Join-Path $projectRoot 'src\NexGenAccountingAI.Web'

if ($NoRestore) {
    dotnet build $solution -c Release --no-restore
}
else {
    dotnet build $solution -c Release
}
if ($LASTEXITCODE -ne 0) { throw 'A compilação falhou.' }

dotnet test $solution -c Release --no-build --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Os testes automatizados falharam.' }

if ($SyntheticOnly) {
    dotnet run --project $smokeProject -c Release -- --synthetic-only
}
else {
    dotnet run --project $smokeProject -c Release -- $examples
}
if ($LASTEXITCODE -ne 0) { throw 'O smoke test falhou.' }

dotnet run --project $uiSmokeProject -c Release
if ($LASTEXITCODE -ne 0) { throw 'O smoke test da interface falhou.' }

Push-Location $webProject
try {
    $webServerIsRunning = $false
    try {
        $webResponse = Invoke-WebRequest -Uri 'http://127.0.0.1:5173/' -UseBasicParsing -TimeoutSec 2
        $webServerIsRunning = $webResponse.StatusCode -eq 200
    }
    catch {
        $webServerIsRunning = $false
    }

    if ($webServerIsRunning) {
        Write-Host 'Servidor Web local detetado; a validar a instalação existente sem substituir ficheiros em uso.'
        npm ls --depth=0
        if ($LASTEXITCODE -ne 0) { throw 'As dependências Web instaladas não correspondem ao package-lock.json.' }
    }
    else {
        npm ci --ignore-scripts
        if ($LASTEXITCODE -ne 0) { throw 'A instalação das dependências web falhou.' }
    }

    npm run lint
    if ($LASTEXITCODE -ne 0) { throw 'A análise estática do frontend falhou.' }

    npm run test
    if ($LASTEXITCODE -ne 0) { throw 'Os testes do frontend falharam.' }

    npm run build
    if ($LASTEXITCODE -ne 0) { throw 'A compilação do frontend falhou.' }
}
finally {
    Pop-Location
}

if ($Publish) {
    $publishDirectory = Join-Path $projectRoot 'artifacts\Accounting-AI'
    # As instalações PHC deste ambiente expõem os DSN SQL através do gestor ODBC de 32 bits.
    dotnet publish $appProject -c Release -r win-x86 --self-contained false -o $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'A publicação falhou.' }
    Write-Host "Aplicação publicada em: $publishDirectory"
}
