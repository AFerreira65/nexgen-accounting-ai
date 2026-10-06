[CmdletBinding()]
param(
    [switch]$Force
)

# Descarrega os esquemas oficiais da AT para a pasta Schemas (que o git ignora).
# Os XSD não estão no repositório porque a sua redistribuição está por confirmar;
# este script vai buscá-los sempre à origem. Com eles na pasta:
#   - a aplicação valida os SAF-T importados contra o esquema oficial (depois de reconstruir);
#   - os testes validam os ficheiros que a aplicação gera (dossier fiscal, etc.).
# Correr outra vez com -Force para atualizar.

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$target = Join-Path $projectRoot 'src\NexGenAccountingAI.Infrastructure.Files\Schemas'
$info = 'https://info.portaldasfinancas.gov.pt'
$support = "$info/pt/apoio_ao_contribuinte/Outras_entidades/Suporte_tecnologico"
$formats = "$support/Formato_de_ficheiros/Contribuintes_e_contabilistas_certificados/Documents"

# Nome local -> origem. Um .zip é aberto e os .xsd que traz ficam na pasta.
$files = [ordered]@{
    'SAFTPT_1.04_01.xsd'  = "$info/apps/saft-pt04/saftpt1.04_01.xsd"
    'SAFTPT_1.03_01.xsd'  = "$info/apps/saft-pt03/saftpt1.03_01.xsd"
    'DossierFiscal.xsd'   = "$support/Dossier_fiscal/Schemas_dossier_fiscal/Documents/DossierFiscal.xsd"
    'dpiva.zip'           = "$formats/Suporte_informatico_IVA_2023.zip"
    'modelo22.zip'        = "$formats/Suporte_informatico_XML_M22_2026.zip"
    'recapitulativa.zip'  = "$formats/dr-1.0.zip"
    # A IES (2027 e seguintes) tem ficheiros com os mesmos nomes dos outros esquemas: vai para a subpasta Ies2027.
    'ies2027.zip'         = "$formats/Suporte_Informatico_IES_2027_seguintes.zip"
}

New-Item -ItemType Directory -Force -Path $target | Out-Null
$failed = @()
foreach ($name in $files.Keys) {
    $url = $files[$name]
    $destination = Join-Path $target $name
    if ($name -notlike '*.zip' -and (Test-Path $destination) -and -not $Force) {
        Write-Host "Já existe: $name"
        continue
    }

    try {
        Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $destination -TimeoutSec 60
        if ($name -like '*.zip') {
            $extract = Join-Path $env:TEMP ("accountingai-schemas-" + [guid]::NewGuid().ToString('N'))
            Expand-Archive -Path $destination -DestinationPath $extract -Force
            $folder = $target
            if ($name -eq 'ies2027.zip') {
                $folder = Join-Path $target 'Ies2027'
                New-Item -ItemType Directory -Force -Path $folder | Out-Null
            }
            Get-ChildItem $extract -Recurse -Filter *.xsd | ForEach-Object {
                Copy-Item $_.FullName (Join-Path $folder $_.Name) -Force
                Write-Host "Descarregado: $($_.Name) (de $name)"
            }
            Remove-Item $extract -Recurse -Force
            Remove-Item $destination -Force
        }
        else {
            Write-Host "Descarregado: $name"
        }
    }
    catch {
        if (Test-Path $destination) { Remove-Item $destination -Force }
        $failed += "$name <- $url ($($_.Exception.Message))"
    }
}

Write-Host ""
Get-ChildItem $target -Filter *.xsd | Sort-Object Name | ForEach-Object { Write-Host ("  {0,-28} {1,9:N0} bytes" -f $_.Name, $_.Length) }
if ($failed.Count -gt 0) {
    Write-Warning "Não foi possível descarregar:"
    $failed | ForEach-Object { Write-Warning "  $_" }
    exit 1
}

Write-Host ""
Write-Host "Reconstrua a solução para a aplicação passar a validar os SAF-T contra o esquema oficial."
