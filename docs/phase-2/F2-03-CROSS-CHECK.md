# F2-03 — Cruzamento: vendas SAF-T × compras e-Fatura × contabilidade

Segundo épico da [Fase 2](README.md). Reconcilia as fontes que hoje vivem
separadas e mostra, por período, onde divergem.

## Contexto

- A contabilidade da empresa está no núcleo (`JournalEntries`/`JournalEntryLines`),
  alimentada por lançamento manual e por importação de SAF-T (F1-53/54).
- O **SAF-T de contabilidade** importado dá os movimentos e os totais
  declarados do período (F1-54: `MatchesDeclaredTotals`). Não traz
  faturas ao nível do documento — isso é o **SAF-T de faturação**
  (`SalesInvoices`), que a nova plataforma **não** importa.
- As **compras do e-Fatura** existem só no monitor WinForms legado
  (`AccountingAI.Core/Efatura/`: `EfaturaCsvImporter` — vários CSV por
  lote, deteção de codificação/separador, canonização de cabeçalhos,
  aviso aos 300 registos; `AtEfaturaPurchaseSource` — consulta SOAP;
  Portal assistido em Edge). **Nada disto está na nova plataforma.**

A lacuna: não há forma de perguntar «as compras que o e-Fatura conhece
estão todas lançadas?» ou «as vendas lançadas batem com o que foi
declarado?».

## Objetivo

Um **relatório de cruzamento** por empresa e período, com três
perspetivas:

1. **Compras** — Σ faturas do e-Fatura (base + IVA) vs. Σ movimentos da
   contabilidade em contas de gasto (classe 6) e IVA dedutível (`2432*`),
   por NIF de fornecedor e por mês; lista das faturas do e-Fatura sem
   correspondência na contabilidade e vice-versa.
2. **Vendas** — Σ movimentos da contabilidade em rendimentos (classe 71/72,
   a crédito) e IVA liquidado (`2433*`) vs. os totais declarados no SAF-T
   de contabilidade do período. (Uma verdadeira reconciliação ao nível do
   documento fica dependente de importar o `SalesInvoices` do SAF-T de
   faturação — F2-03E, opcional.)
3. **SAF-T vs. contabilidade** — reaproveita a reconciliação de F1-54 (os
   débitos/créditos da empresa no período do ficheiro vs. `TotalDebit`/
   `TotalCredit`), agora visível como parte do mesmo relatório.

O relatório é um apoio de conferência; não altera lançamentos.

## Fontes e chaves

- **Fatura do e-Fatura** — chave de correspondência: `NIF do emitente` +
  `InvoiceNo` (normalizado) + `InvoiceDate`. Campos usados: emitente,
  NIF, data, tipo, base tributável, IVA, total. O ATCUD e o nº vêm juntos
  no CSV do Portal («Nº Fatura / ATCUD») e são separados na leitura.
- **Movimento da contabilidade** — um lançamento é «de compra» quando tem
  linhas em `6*` e/ou `2432*` e uma linha em `2211*`/`221*` (fornecedor);
  a ligação à fatura do e-Fatura faz-se pelo NIF do fornecedor (conta
  corrente) + `DocumentReference` ≈ `InvoiceNo`, com tolerância.
- **Período** — mês de calendário; o relatório cobre um intervalo de
  meses do exercício.

## Desenho

### Domínio / Application

```
Domain/Efatura/
  EFaturaPurchase.cs           // registo de leitura de uma fatura do e-Fatura
Application/Efatura/
  IEFaturaCsvReader.cs         // Task<EFaturaCsvResult> ReadAsync(Stream)
  EFaturaCsvResult.cs          // faturas + lotes + avisos (300)
Application/CrossCheck/
  CrossCheckReport.cs          // as três perspetivas + as listas de divergências
  ICrossCheckService.cs
```

### Infrastructure

- `EFaturaCsvReader` (`Infrastructure.Files`) — porta a leitura do
  `EfaturaCsvImporter` legado (encoding, separador, cabeçalhos
  canónicos, aviso aos 300), em streaming, sem `Microsoft.VisualBasic`.
- `CrossCheckService` (`Infrastructure.SqlServer`) — junta as faturas
  persistidas com os movimentos do período e produz o `CrossCheckReport`.
- Tabela `accounting.EFaturaPurchase` (+ `EFaturaImportBatch`), migração
  EF Core. Importação **idempotente** pela chave acima.

### API

- `POST .../companies/{companyId}/efatura/purchases-import` — corpo = CSV
  (`text/csv`); pré-visualização (`?dryRun=true`) e importação; devolve
  criadas / já existentes / linhas inválidas, e os avisos de lote.
- `GET .../companies/{companyId}/efatura/purchases?from=&to=` — lista
  paginada (como os movimentos).
- `GET .../companies/{companyId}/cross-check?from=&to=` — o
  `CrossCheckReport`.

### PWA

- Ecrã **«Compras e-Fatura»** — importar um ou mais CSV (pré-visualizar,
  importar, avisos de lote), e listar por período/fornecedor, paginado.
- Ecrã **«Cruzamento»** — as três perspetivas com os totais por mês e as
  listas de divergências, cada linha com ligação ao movimento ou à fatura;
  exportação CSV.
- No painel e/ou nas Tarefas: «N faturas do e-Fatura sem correspondência».

## Sub-incrementos

### F2-03A — Ingestão das compras do e-Fatura (CSV)

- **Regras:** `EFaturaPurchase` + `EFaturaImportBatch` no domínio; tabela e
  migração; `IEFaturaCsvReader` + `EFaturaCsvReader` (porta do legado, sem
  `Microsoft.VisualBasic`); `POST .../efatura/purchases-import`
  (`?dryRun`) idempotente pela chave NIF+InvoiceNo+data; `GET
  .../efatura/purchases`.
- **Validação:** testes de leitura de CSV (separador `;` e `,`, BOM,
  Windows-1252, aviso aos 300); teste de escrita idempotente na suíte de
  integração; teste de API de leitura.

### F2-03B — Ecrã de compras e-Fatura na PWA

- **Regras:** ecrã de importação (multi-CSV, pré-visualização, avisos) e
  lista paginada por período/fornecedor; rótulos pt-PT.
- **Validação:** testes de interface do fluxo de importação e da lista.

### F2-03C — Relatório de cruzamento

- **Regras:** `CrossCheckService` + `GET .../cross-check`; as três
  perspetivas com totais por mês e listas de divergências (e-Fatura sem
  contabilidade, contabilidade sem e-Fatura, vendas contab. vs. declarado).
- **Validação:** testes do serviço com cenários construídos (fatura só no
  e-Fatura, só na contabilidade, valores diferentes); teste de API.

### F2-03D — Ecrã de cruzamento + sinais

- **Regras:** ecrã «Cruzamento» com as três perspetivas e as ligações à
  origem; exportação CSV; contador nas Tarefas/painel.
- **Validação:** testes de interface.

### F2-03E — (opcional) `SalesInvoices` do SAF-T de faturação

- **Regras:** leitor em streaming do bloco `SourceDocuments/SalesInvoices`;
  passa a haver uma fonte de vendas ao nível do documento para a
  perspetiva 2 do cruzamento.
- **Validação:** testes de leitura + reconciliação.

## Decisões em aberto

- **Só CSV, ou também a consulta SOAP / Portal assistido?** O CSV é
  portável e sem dependências; a consulta oficial e o Portal assistido
  trazem browser embebido, credenciais e sessão — proponho ficarem **fora**
  do âmbito da nova plataforma web (mantêm-se no monitor legado).
- **Tolerância de correspondência** entre `DocumentReference` e
  `InvoiceNo` (formatos divergentes por software emissor) — precisa de
  ensaio com dados reais.
- **Onde entra o ecrã de compras** — hub de importação partilhado com o
  SAF-T, ou ecrã próprio.

## Referências

- `AccountingAI.Core/Efatura/` — a implementação legada a portar.
- `README.md` §«Compras do e-Fatura» — o comportamento do monitor.
- F1-53/F1-54 — importação e reconciliação do SAF-T de contabilidade.
