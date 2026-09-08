# F2-03B — Ecrã de compras e-Fatura na PWA

Segundo sub-incremento do [F2-03](F2-03-CROSS-CHECK.md), sobre a ingestão de
[F2-03A](F2-03A-EFATURA-CSV-INGESTION.md).

## Regras

- Nova rota `/compras-efatura` e entrada no menu lateral («Compras
  e-Fatura», a seguir a «Importar SAF-T»).
- `WorkContext.importEFaturaCsv(companyId, file, dryRun)` e
  `eFaturaPurchases(companyId, { from, to, page, pageSize })` — ligam aos
  endpoints de F2-03A.
- **Painel de importação:** escolher um ou mais CSV; cada ficheiro é
  **pré-visualizado** (`dryRun`) numa tabela — a importar / já existiam /
  inválidas / avisos (300 registos, CSV ilegível) — com um botão «Importar»
  por ficheiro e um «Importar todos». Depois de gravar, a lista atualiza.
- **Painel da lista:** intervalo de datas (arranca no exercício da empresa,
  com atalhos de período e «Limpar datas»), paginação do lado do servidor
  pelo tamanho de página das preferências, colunas Data / Fornecedor (NIF) /
  Fatura (ATCUD) / Tipo / Base / IVA / Total.
- Fora do modo de base de dados, o ecrã mostra «Requer ligação ao SQL
  Server» (as compras vivem na base da empresa).

Sem alteração de esquema nem de API. A fase do `/foundation` mantém-se em
`F2-01`.

## Superfície de validação local

Abrir `/compras-efatura` numa empresa ligada à BD, escolher um CSV do
Portal, pré-visualizar e importar; a lista mostra as faturas do período.

## Validação efetuada

- `AccountingAI.Web`: lint, build e setenta e quatro testes de interface
  (um novo — a rota `/compras-efatura` mostra o cabeçalho e, em
  demonstração, «Requer ligação ao SQL Server»);
- `dotnet build`/`dotnet test` na solução inteira sem falhas (115 testes
  .NET, sem alteração — incremento de frontend);
- `gitleaks` sem segredos;
- verificação no browser contra a base `accountingAI`, na Almeida & Costa:
  escolher um CSV de duas faturas mostra a pré-visualização «A importar 2»;
  «Importar» grava e a lista passa a mostrar as duas faturas com os valores
  certos (base, IVA, total). As linhas de teste foram removidas da base a
  seguir.

## Fora deste incremento

- o relatório de cruzamento (`CrossCheckService` + `/cross-check`) — F2-03C;
- exportação CSV da lista (o cruzamento em F2-03D terá exportação própria).
