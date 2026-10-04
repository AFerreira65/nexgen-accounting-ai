# Correr a Accounting AI como serviço do Windows

Como pôr a API (com a PWA embutida) sempre ativa, sem precisar de um
terminal aberto. Ver [F2-11](phase-2/F2-11-AUTHENTICATION.md) e
[ADR-0011](architecture-decisions/ADR-0011-AUTHENTICATION.md) para o porquê
— isto só faz sentido depois de a autenticação por sessão estar em uso, já
que sem ela a API ficava exposta 24 horas por dia sem proteção nenhuma.

A instalação do serviço em si precisa de **administrador** — os passos 3 em
diante têm de ser feitos por si, num PowerShell "Executar como
administrador". O resto (publicar) não precisa.

## 1. Publicar

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish-service.ps1
```

Compila a PWA em modo de produção, copia-a para
`src\AccountingAI.Api\wwwroot` (a API passa a servi-la ela própria — sem
precisar do servidor de desenvolvimento do Vite) e publica a API em
`artifacts\AccountingAI-Service\`. Para repetir isto sempre que o código
mudar, corra o script outra vez — substitui o publicado anterior.

## 2. Aplicar as migrações

Em produção (`Database:ApplyMigrationsOnStartup` fica `false` de propósito —
ver `appsettings.Production.json`), aplique-as uma vez, à mão, antes do
primeiro arranque e sempre que houver migrações novas:

```powershell
& "artifacts\AccountingAI-Service\AccountingAI.Api.exe" --migrate-only
```

## 3. Criar o serviço (administrador)

```powershell
sc.exe create AccountingAI binPath= "`"$PWD\artifacts\AccountingAI-Service\AccountingAI.Api.exe`"" start= auto DisplayName= "Accounting AI"
sc.exe description AccountingAI "API e site da Accounting AI, sempre ativos em http://127.0.0.1:5080."
```

(O caminho tem de ser absoluto — confirme que está na pasta do projeto antes
de correr isto, ou substitua `$PWD` pelo caminho completo.)

## 4. Escolher a conta com que o serviço corre — o passo que não pode saltar

**Por omissão, um serviço criado assim corre como `LocalSystem` — e isso não
funciona aqui.** Duas coisas na aplicação estão ligadas à sua conta pessoal
do Windows:

- a password do papel `accounting` do PostgreSQL não está em
  `appsettings.Production.json`: defina a ligação completa na variável de
  ambiente `ConnectionStrings__Accounting` (ou `PGPASSWORD`) da conta do serviço;
- a senha do certificado da AT, se a vier a configurar, cifra-se com a Data
  Protection do ASP.NET Core, cujo chaveiro fica em
  `%LOCALAPPDATA%\AccountingAI\dp-keys` — um caminho por conta. Correr como
  outra conta (incluindo `LocalSystem`) não consegue decifrá-la.

Mude a conta pela consola de Serviços (mais seguro — não deixa a senha no
histórico do terminal):

1. `services.msc` → **Accounting AI** → Propriedades → separador **Log On**.
2. **This account** → indique o seu utilizador do Windows (`.\<utilizador>`)
   e a sua senha → OK.
3. Reinicie o serviço (o próprio painel de Serviços oferece isso).

## 5. Arrancar e verificar

```powershell
sc.exe start AccountingAI
sc.exe query AccountingAI
```

Abra `http://127.0.0.1:5080` no browser — deve mostrar a Accounting AI a
sério (não só o `/api/v1/foundation`), com o ecrã de login se não tiver
sessão.

## Rede

O serviço escuta só em `127.0.0.1` (`Urls` em `appsettings.Production.json`)
— só quem está fisicamente nesta máquina consegue lá chegar, mesmo que a
Firewall do Windows não bloqueie nada. Não é preciso abrir nenhuma regra de
Firewall para uso local.

## Parar / desinstalar

```powershell
sc.exe stop AccountingAI
sc.exe delete AccountingAI
```

## Se a senha de alguém mudar, ou for preciso criar mais um utilizador

Continua a ser só por linha de comandos — nunca por um formulário público:

```powershell
& "artifacts\AccountingAI-Service\AccountingAI.Api.exe" set-user-password "<email>" "<nova senha>"
```

## Voltar a correr em modo de desenvolvimento

Nada disto obriga a usar o serviço sempre — `dotnet run` continua a
funcionar em `http://localhost:5080` como antes, com a Vite em
`http://localhost:5173`. As duas formas usam a mesma base de dados.
