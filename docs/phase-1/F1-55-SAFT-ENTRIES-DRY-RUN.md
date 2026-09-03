# F1-55 — Analisar a importação do SAF-T antes de escrever

## Objetivo

Antes de importar os movimentos de um SAF-T (F1-53/F1-54), dizer ao contabilista o
que aconteceria: quantas transações entrariam, quantas seriam saltadas e porquê,
que diários seriam criados e se os totais reconciliariam — sem escrever nada, para
dar tempo de corrigir o plano de contas ou reabrir um exercício.

## Regras

- A classificação por transação da importação foi extraída para um método
  partilhado (`ClassifyTransaction`): as mesmas regras (referência única, data,
  período bloqueado, exercício apurado, diário, número e forma das linhas,
  equilíbrio, contas existentes/ativas/de movimento, mais uma construção de teste
  pelo domínio) servem agora tanto `ImportAsync` como `AnalyzeAsync`.
- `AnalyzeAsync` corre essa classificação sobre todas as transações **sem abrir
  transação de base de dados nem escrever**. Devolve `SaftEntriesAnalysis`:
  `WouldImportCount`, `WouldSkipCount`, `Skipped` (com a razão em pt-PT),
  `JournalsToCreate`, os totais declarados, o período do ficheiro,
  `ProjectedPeriodDebit`/`ProjectedPeriodCredit` (o que a empresa já tem no
  período **mais** o que entraria) e `WouldMatchDeclaredTotals` (`true` quando
  esses totais previstos coincidem, a 2 casas, com os declarados; `null` sem
  totais declarados).
- Endpoint só em `Development`, **sem escrita**:
  `POST /api/v1/development/users/{userId}/companies/{companyId}/saft/entries-analyze`
  — corpo `application/xml`; `404` se o utilizador não estiver associado à
  empresa; `400` (`ValidationProblem`, chave `saft`) se o ficheiro não for
  legível.
- No ecrã de importação, o painel «Movimentos do ficheiro» ganha o botão
  «Analisar importação» (só com ligação ao SQL Server), entre a pré-visualização e
  a importação. Mostra a previsão «Seriam importados N; diários a criar; M
  saltados», a linha de reconciliação prevista e a tabela das transações que
  seriam saltadas. Ao importar, o resultado real substitui a previsão.

Sem alteração de esquema.

## Superfície de validação local

Escolher um SAF-T com movimentos, pré-visualizar e usar «Analisar importação». A
previsão diz o que a importação faria; nada é escrito. A seguir, «Importar os N
movimentos» produz o resultado real, que deve bater certo com a previsão.

## Validação efetuada

- build .NET Debug sem avisos nem erros; suite .NET completa (72 testes);
- `AccountingAI.IntegrationTests` (LocalDB descartável): antes de importar,
  `AnalyzeAsync` prevê `WouldImportCount = 2`, `WouldSkipCount = 3`,
  `JournalsToCreate = ["CX"]`, `ProjectedPeriodDebit = 150` e
  `WouldMatchDeclaredTotals = true`, e a base fica intacta (a importação a seguir
  confirma os mesmos números); sem associação à empresa devolve `null`; analisar
  depois de importar dá `WouldImportCount = 0` mantendo a reconciliação prevista;
- verificação no browser contra a base `accountingAI`: com um SAF-T sintético para
  a empresa da ilha (exercício aberto, dois movimentos válidos + um
  desequilibrado, um diário novo, totais declarados 160), «Analisar importação»
  mostra «Seriam importados 2 movimentos, 1 diário a criar (CXUI), 1 saltado» e
  «Depois de importar, os totais … (160,00 €) coincidiriam com os declarados»,
  sem criar o diário nem os lançamentos; a importação a seguir produz o mesmo
  resultado real; os dados de teste foram removidos da base no fim;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos;
- lint, trinta e dois testes de interface e build de produção da PWA.

## Fora deste incremento

- analisar dentro da própria pré-visualização, sem um segundo botão;
- prever a numeração/referências que a importação atribuiria;
- guardar a análise para comparar com o resultado da importação.
