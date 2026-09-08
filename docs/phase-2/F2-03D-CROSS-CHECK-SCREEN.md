# F2-03D — Ecrã de cruzamento e sinal nas Tarefas

Quarto sub-incremento do [F2-03](F2-03-CROSS-CHECK.md), sobre o serviço e o
endpoint de [F2-03C](F2-03C-CROSS-CHECK-REPORT.md).

## Regras

- Nova rota `/cruzamento` e entrada no menu lateral («Cruzamento», a seguir a
  «Compras e-Fatura»).
- `WorkContext.crossCheck(companyId, { from, to })` — liga ao `GET
  .../cross-check` de F2-03C.
- **Painel «Período»** — intervalo de datas que arranca no exercício da
  empresa, com atalhos (Mês / Trimestre / Ano). O endpoint exige as duas
  datas; sem elas o ecrã pede-as e não consulta.
- **Painel «Compras»** — o resumo (nº de faturas do e-Fatura, nº de
  lançamentos de compra, quantos com correspondência) e um selo com os desvios
  de base e IVA; a tabela por mês com e-Fatura base/IVA, contabilidade
  base/IVA e Δ base/Δ IVA.
- **Painel «Faturas do e-Fatura sem correspondência»** — cada linha com data,
  fornecedor (NIF), fatura (ATCUD), base, IVA, total e uma ligação ao ecrã de
  compras e-Fatura.
- **Painel «Lançamentos de compra sem fatura no e-Fatura»** — cada linha com
  data, documento, referência, descrição, base, IVA dedutível, total e uma
  ligação ao movimento.
- **Painel «Vendas»** — rendimentos e IVA liquidado da contabilidade por mês
  (apoio de conferência; a conferência ao nível do documento depende do SAF-T
  de faturação — F2-03E).
- **Painel «Totais do período»** — débito e crédito dos lançamentos não
  anulados, com o selo «débito = crédito».
- **Exportação CSV** — um único ficheiro com as linhas por mês das compras, as
  duas listas de divergências e as vendas por mês (`csvFilename`/`toCsv`/
  `downloadCsv`, separador `;`, vírgula decimal, BOM UTF-8).
- Fora do modo de base de dados, o ecrã mostra «Requer ligação ao SQL Server».
- **Tarefas** — quando há faturas do e-Fatura sem correspondência no exercício,
  um painel «Faturas do e-Fatura sem correspondência» com a contagem e a
  ligação a `/cruzamento`. A consulta é silenciosa (um cruzamento indisponível
  não parte as Tarefas) e só corre numa empresa ligada à base de dados.

## Afinação em F2-03C (incluída aqui)

O apuramento de resultados e a abertura contra-movimentam as classes 6 e 7 sem
serem operações reais e distorciam as três perspetivas (apareciam meses com
base e rendimentos negativos). O `CrossCheckService` passou a excluir os
lançamentos de origem `Opening` e `Closing` das compras, das vendas e dos
totais do período. Sem alteração de esquema nem de contrato; a fase do
`/foundation` mantém-se em `F2-01`.

## Validação efetuada

- `AccountingAI.Web`: lint, build e setenta e cinco testes de interface (um
  novo — a rota `/cruzamento` mostra o cabeçalho e, em demonstração, «Requer
  ligação ao SQL Server»);
- `dotnet build`/`dotnet test` na solução inteira sem falhas (cento e vinte e
  quatro testes .NET — a exclusão de abertura/apuramento não altera as
  asserções de forma do teste de API);
- `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem
  ocorrências;
- verificação no browser contra `accountingAI` / Almeida & Costa: com o ano de
  2026 escolhido, o cruzamento mostra Agosto com base 1200 / IVA 276 na
  contabilidade e o lançamento `COMP-2026-00842` na lista «sem fatura no
  e-Fatura»; importar um CSV com `FT 2026/418` (2026-08-29) e `FS 9/12`
  (2026-09-04) passa a mostrar `matchedCount = 1`, o desvio de Setembro
  (-80,00 / -18,40) e a fatura `FS 9/12` na lista «sem correspondência»; as
  Tarefas passam a mostrar «Faturas do e-Fatura sem correspondência: 1». As
  linhas de teste foram removidas da base a seguir.

## Fora deste incremento

- o leitor de `SalesInvoices` do SAF-T de faturação — F2-03E (opcional), que
  fecha o épico.
