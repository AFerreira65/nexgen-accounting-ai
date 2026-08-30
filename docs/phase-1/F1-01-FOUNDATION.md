# F1-01 — Fundação da solução

## Estado

Implementada e validada localmente em 29 de agosto de 2026.

Esta tarefa cria apenas a fundação técnica da nova plataforma contabilística. Não implementa identidade, empresas, plano de contas, lançamentos, SAF-T, e-Fatura, PHC, IA ou declarações fiscais.

## Entregas

- monólito modular em .NET 10 com `Domain`, `Application`, `Infrastructure.SqlServer`, `Api` e `Worker`;
- PWA React/TypeScript independente do monitor WinForms;
- API com endpoints de liveness, readiness e identificação da fase;
- correlação de pedidos pelo cabeçalho `X-Correlation-ID` e logs JSON estruturados;
- persistência SQL Server com primeira migração EF Core;
- migração automática apenas quando explicitamente configurada;
- testes de arquitetura, API e migrações SQL Server LocalDB;
- lint, testes e build do frontend;
- comando único de validação, preservando os smoke tests do monitor existente.

## Fronteiras arquiteturais

```text
Domain <- Application <- Infrastructure.SqlServer <- Api
                                      ^
                                      └────────────── Worker
```

Regras verificadas automaticamente:

- `Domain` não tem dependências de packages ou de outros projetos;
- `Application` conhece apenas `Domain`;
- a infraestrutura conhece `Application` e `Domain`;
- `Api` e `Worker` consomem as camadas internas sem inverter dependências;
- a nova plataforma não depende de `AccountingAI.Core` nem de `AccountingAI.WinForms` nesta fase.

## Executar a validação completa

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

O comando executa:

1. build Release da solução;
2. testes de arquitetura, API e integração SQL Server;
3. smoke tests existentes de Core e WinForms;
4. instalação determinística das dependências web;
5. lint, testes e build de produção da PWA.

## Executar a API localmente

```powershell
dotnet run --project .\src\AccountingAI.Api\AccountingAI.Api.csproj
```

Endpoints locais:

- `http://localhost:5080/health/live` — processo disponível;
- `http://localhost:5080/health/ready` — SQL Server acessível;
- `http://localhost:5080/api/v1/foundation` — identificação da fundação.

A configuração de desenvolvimento usa SQL Server LocalDB e aplica a migração no arranque. Noutros ambientes deve ser fornecida `ConnectionStrings__Accounting` por secret store ou variável protegida.

## Aplicar migrações

Desenvolvimento com LocalDB:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\migrate.ps1
```

Outro SQL Server, sem guardar a connection string no repositório:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\migrate.ps1 -ConnectionString '<connection-string-obtida-do-secret-store>'
```

## Executar a PWA

```powershell
Set-Location .\src\AccountingAI.Web
npm ci --ignore-scripts
npm run dev
```

O servidor de desenvolvimento encaminha `/api` e `/health` para a API em `http://localhost:5080`.

## Limites da validação

> Nota histórica: estes limites descrevem o encerramento da F1-01. A experiência contabilística local e o cadastro multiempresa foram posteriormente iniciados nas tarefas F1-02A a F1-02C.

- LocalDB comprova a compatibilidade local das migrações; não substitui testes em SQL Server de staging ou produção.
- Os health checks não constituem monitorização de produção.
- A PWA contém apenas o ecrã de fundação, não uma interface contabilística.
- Não foi executado qualquer workflow remoto nem utilizado um ambiente de cliente.
- No encerramento desta tarefa, F1-02 e tarefas posteriores ainda não tinham sido iniciadas.
