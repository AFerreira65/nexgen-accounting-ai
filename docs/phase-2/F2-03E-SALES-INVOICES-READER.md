# F2-03E — Leitor do `SalesInvoices` do SAF-T de faturação

Quinto e último sub-incremento do [F2-03](F2-03-CROSS-CHECK.md). Dá ao
cruzamento uma fonte de vendas ao nível do documento e **fecha o épico**.

## Regras

- `ISaftSalesInvoicesReader` / `SaftSalesInvoicesReader` (`Infrastructure.Files`,
  fronteira em `Application/Saft`) — lê o bloco
  `SourceDocuments/SalesInvoices` de um SAF-T (PT) de faturação totalmente em
  streaming (DTD proibido, `XmlResolver` nulo, Windows-1252), uma
  `<Invoice>` como subárvore de cada vez. Devolve `SaftSalesInvoicesSummary`:
  os totais declarados no cabeçalho do bloco (`NumberOfEntries`, `TotalDebit`,
  `TotalCredit`), a contagem de faturas e de anuladas, as somas de base
  (`NetTotal`), IVA (`TaxPayable`) e total (`GrossTotal`) **sem as anuladas**
  (`InvoiceStatus` = `A`), o intervalo de datas e a soma por mês de calendário.
  Um ficheiro sem o bloco devolve `HasSalesInvoices = false`; XML mal formado
  lança `SaftFileFormatException`.
- `POST .../companies/{companyId}/saft/sales-invoices-summary` — corpo = XML;
  `HasCompanyAccessAsync` (`404`), `SaftFileFormatException` → `400`. Devolve o
  resumo, com os meses.
- **PWA** — no ecrã «Cruzamento», um painel «SAF-T de faturação (opcional)»:
  escolher o ficheiro chama o endpoint e mostra uma tabela por mês com a base
  do SAF-T, os rendimentos da contabilidade (do próprio relatório de
  cruzamento) e os desvios de base e de IVA; a linha de resumo traz a contagem,
  as somas e o `TotalCredit` declarado. Um aviso quando o SAF-T não cobre
  nenhum mês do período escolhido.

Sem alteração de esquema nem de contrato do `/foundation` (mantém-se `F2-01`).
O leitor não escreve nada.

## Superfície de validação local

```
POST /api/v1/development/users/33333333-3333-3333-3333-333333333333/companies/{companyId}/saft/sales-invoices-summary
Content-Type: application/xml
<AuditFile>…<SourceDocuments><SalesInvoices>…</SalesInvoices></SourceDocuments></AuditFile>
```

## Validação efetuada

- `AccountingAI.Infrastructure.Files.Tests`: quatro testes novos do
  `SaftSalesInvoicesReader` (resumo por mês + totais declarados; anuladas
  excluídas mas contadas; ficheiro sem o bloco; XML mal formado);
- `AccountingAI.Api.Tests`: um teste novo — o endpoint lê o bloco, está
  limitado à empresa do utilizador (`404`) e trata o XML mal formado (`400`);
- `AccountingAI.Web`: lint, build e setenta e cinco testes de interface (sem
  novos — o painel só aparece com ligação à base de dados);
- `dotnet build`/`dotnet test` na solução inteira sem falhas (cento e vinte e
  nove testes .NET);
- `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem
  ocorrências;
- verificação no browser contra `accountingAI` (empresa Vidreira Algarvia, com
  o exercício de 2025 e milhares de lançamentos): carregar um SAF-T de
  faturação sintético mostra a linha de resumo (2 faturas, base e IVA, e o
  `TotalCredit` declarado) e a tabela por mês em ordem de calendário, com os
  desvios face aos rendimentos já no relatório; um SAF-T fora do período
  mostra o aviso.

## Fecho do épico F2-03

Com F2-03A–E o cruzamento cobre: ingestão das compras do e-Fatura por CSV,
ecrã de compras, relatório de cruzamento (compras e-Fatura × contabilidade,
vendas da contabilidade por mês, totais do período), ecrã «Cruzamento» com
sinal nas Tarefas, e agora a leitura das vendas ao nível do documento do
SAF-T de faturação. Fica em aberto do épico apenas a afinação da tolerância
de correspondência `DocumentReference` ↔ `InvoiceNo` com dados reais de
vários softwares emissores.
