<#
.SYNOPSIS
  Junta o certificado assinado pela AT à chave privada gerada por new-at-csr.ps1 e cria o PFX
  que se carrega no ecrã «Configuração geral» da conta de produtor.

.DESCRIPTION
  Confere que o certificado corresponde à chave, que o NIF (CN) é o do produtor e que não expirou;
  só então cria o PFX. A senha do PFX é pedida aqui (não aparece no ecrã nem no histórico) e é a que
  depois se escreve na aplicação: guarde-a num sítio seguro, não fica registada em lado nenhum.

.EXAMPLE
  .\scripts\assemble-at-pfx.ps1 -Certificate C:\Downloads\518104486.crt
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Certificate,
    [string]$TaxNumber = '518104486',
    [string]$Dir = (Join-Path $env:LOCALAPPDATA 'AccountingAI\at-producao'),
    # Só para automatismos; sem ele a senha é pedida (e confirmada) no terminal.
    [securestring]$Password
)

$ErrorActionPreference = 'Stop'

$openssl = (Get-Command openssl -ErrorAction SilentlyContinue).Source
if (-not $openssl) {
    $openssl = @('C:\Program Files\Git\mingw64\bin\openssl.exe', 'C:\Program Files\Git\usr\bin\openssl.exe') |
        Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $openssl) { throw 'Não encontrei o OpenSSL.' }

# Uma variável OPENSSL_CONF a apontar para um ficheiro que não existe (acontece com instalações do PostgreSQL) faz o OpenSSL falhar.
if ($env:OPENSSL_CONF -and -not (Test-Path $env:OPENSSL_CONF)) { Remove-Item Env:\OPENSSL_CONF }

$key = Join-Path $Dir "$TaxNumber.key"
$pfx = Join-Path $Dir "$TaxNumber.pfx"
if (-not (Test-Path $Certificate)) { throw "Não encontro o certificado: $Certificate" }
if (-not (Test-Path $key)) { throw "Não encontro a chave privada: $key (gerada por new-at-csr.ps1 nesta máquina)." }
if (Test-Path $pfx) { throw "Já existe $pfx. Mova-o ou apague-o antes de criar outro." }

# O certificado vem por vezes em DER; passa-se a PEM para os testes seguintes.
$pem = Join-Path $Dir "$TaxNumber.crt.pem"
& $openssl x509 -in $Certificate -out $pem 2>$null
if ($LASTEXITCODE -ne 0) { & $openssl x509 -inform DER -in $Certificate -out $pem }
if ($LASTEXITCODE -ne 0) { throw 'O ficheiro não é um certificado X.509 (PEM ou DER).' }

$subject = (& $openssl x509 -in $pem -noout -subject) -join ''
$notAfter = ((& $openssl x509 -in $pem -noout -enddate) -join '') -replace '^notAfter=', ''
if ($subject -notmatch "CN\s*=\s*$TaxNumber") { throw "O certificado não é do NIF ${TaxNumber}: $subject" }

$certModulus = (& $openssl x509 -in $pem -noout -modulus) -join ''
$keyModulus = (& $openssl rsa -in $key -noout -modulus) -join ''
if ($certModulus -ne $keyModulus) { throw 'Este certificado NÃO corresponde à chave privada guardada: foi emitido para outro pedido.' }

$first = if ($Password) { $Password } else { Read-Host 'Senha para o PFX (>= 12 caracteres)' -AsSecureString }
$second = if ($Password) { $Password } else { Read-Host 'Repita a senha' -AsSecureString }
function ToPlain([securestring]$s) { [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($s)) }
$plain = ToPlain $first
if ($plain -ne (ToPlain $second)) { throw 'As senhas são diferentes.' }
if ($plain.Length -lt 12) { throw 'A senha tem de ter pelo menos 12 caracteres.' }

$env:AT_PFX_PASSWORD = $plain
try {
    & $openssl pkcs12 -export -in $pem -inkey $key -out $pfx -passout env:AT_PFX_PASSWORD
    if ($LASTEXITCODE -ne 0) { throw 'O OpenSSL falhou a criar o PFX.' }
}
finally {
    Remove-Item Env:\AT_PFX_PASSWORD -ErrorAction SilentlyContinue
    $plain = $null
}

Write-Host ''
Write-Host "PFX criado: $pfx" -ForegroundColor Green
Write-Host "Certificado: $subject"
Write-Host "Válido até: $notAfter (12 meses: apontar a renovação)"
Write-Host 'Carregue o PFX e a senha em «Configuração geral» (conta de produtor). Guarde uma cópia do PFX e da senha em local seguro; depois pode apagar a chave .key solta.'
