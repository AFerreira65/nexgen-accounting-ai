<#
.SYNOPSIS
  Gera a chave privada e o pedido de certificado (CSR) para a adesão do produtor de software
  aos webservices da AT ("Aderir ao Serviço", Portal das Finanças > e-Fatura > Produtores de Software).

.DESCRIPTION
  Segue os manuais de integração da AT (secção "Assinatura certificado SSL (CSR)"): chave RSA de
  4096 bits, campos sem acentos e dentro dos tamanhos máximos, CN = NIF do produtor.
  A chave privada fica SÓ nesta máquina, numa pasta fora do repositório com acesso apenas do
  utilizador atual, e nunca é enviada: à AT vai só o ficheiro .csr.
  Não substitui uma chave existente (perderia o certificado já emitido para ela).

.EXAMPLE
  .\scripts\new-at-csr.ps1 -State Faro -Locality Loule -Organisation "Empresa Lda" `
      -Department "Informatica" -Email "informatica@empresa.pt"
#>
[CmdletBinding()]
param(
    [string]$TaxNumber = '518104486',
    [Parameter(Mandatory)][string]$State,
    [Parameter(Mandatory)][string]$Locality,
    [Parameter(Mandatory)][string]$Organisation,
    [Parameter(Mandatory)][string]$Department,
    [Parameter(Mandatory)][string]$Email,
    [string]$Country = 'PT',
    # O formulário «Aderir ao Serviço» recusa 2048 («tem de ser uma chave RSA de 4096 bits»), embora os
    # manuais da AT ainda digam 2048. Vale o formulário.
    [ValidateSet(2048, 3072, 4096)][int]$Bits = 4096,
    [string]$OutDir = (Join-Path $env:LOCALAPPDATA 'AccountingAI\at-producao')
)

$ErrorActionPreference = 'Stop'

function Assert-Field([string]$name, [string]$value, [int]$max) {
    if ($value -notmatch '^[\x20-\x7E]+$') {
        throw "$name tem caracteres que a AT não aceita (acentos, cedilhas…). Escreva sem acentos: '$value'"
    }
    if ($value -match '[/\\=+"]') {
        throw "$name não pode conter / \ = + nem aspas: '$value'"
    }
    if ($value.Length -gt $max) {
        throw "$name excede $max caracteres ($($value.Length))."
    }
}

if ($TaxNumber -notmatch '^\d{9}$') { throw 'O NIF tem de ter 9 algarismos.' }
if ($Country -notmatch '^[A-Z]{2}$') { throw 'O país tem de ter 2 letras maiúsculas (ISO 3166-1), por exemplo PT.' }
if ($Email -notmatch '^[^@\s]+@[^@\s]+\.[^@\s]+$') { throw 'O email não é válido.' }
Assert-Field 'Distrito (ST)' $State 32
Assert-Field 'Local (L)' $Locality 32
Assert-Field 'Designacao legal (O)' $Organisation 180
Assert-Field 'Departamento (OU)' $Department 180
Assert-Field 'Email (E)' $Email 80

$openssl = (Get-Command openssl -ErrorAction SilentlyContinue).Source
if (-not $openssl) {
    $openssl = @('C:\Program Files\Git\mingw64\bin\openssl.exe', 'C:\Program Files\Git\usr\bin\openssl.exe') |
        Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $openssl) { throw 'Não encontrei o OpenSSL. Instale-o (ou o Git for Windows, que o traz).' }

# Uma variável OPENSSL_CONF a apontar para um ficheiro que não existe (acontece com instalações do PostgreSQL) faz o OpenSSL falhar.
if ($env:OPENSSL_CONF -and -not (Test-Path $env:OPENSSL_CONF)) { Remove-Item Env:\OPENSSL_CONF }

New-Item -ItemType Directory -Force $OutDir | Out-Null
$key = Join-Path $OutDir "$TaxNumber.key"
$csr = Join-Path $OutDir "$TaxNumber.csr"
if (Test-Path $key) {
    throw "Já existe $key. Não a substituo: se o certificado já foi pedido com ela, perdia-o. Apague-a manualmente só se tiver a certeza."
}

# Só o utilizador atual acede à pasta (a chave fica sem senha, como no manual da AT, até ser guardada no PFX).
$me = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
& icacls $OutDir /inheritance:r /grant:r "${me}:(OI)(CI)F" | Out-Null

$subject = "/C=$Country/ST=$State/L=$Locality/O=$Organisation/OU=$Department/CN=$TaxNumber/emailAddress=$Email"
& $openssl req -new -subj $subject -newkey "rsa:$Bits" -nodes -out $csr -keyout $key
if ($LASTEXITCODE -ne 0) {
    Remove-Item $key, $csr -ErrorAction SilentlyContinue
    throw 'O OpenSSL falhou.'
}

Write-Host ''
Write-Host 'Conteúdo do pedido (confirme antes de enviar):' -ForegroundColor Cyan
& $openssl req -text -noout -in $csr | Select-String -Pattern 'Subject:|Public-Key:'
Write-Host ''
Write-Host "Pedido (CSR, para colar no formulário da AT): $csr" -ForegroundColor Green
Write-Host "Chave privada (NÃO enviar a ninguém, fazer cópia de segurança): $key" -ForegroundColor Yellow
Write-Host 'Quando a AT devolver o certificado assinado, use scripts\assemble-at-pfx.ps1.'
