# F1-58 — Exportar a lista de movimentos para CSV

## Objetivo

Os relatórios exportam CSV (F1-15); a lista de movimentos não. Depois de a
restringir a um período (F1-57), o contabilista quer levar essas linhas para a
folha de cálculo.

## Regras

- A barra de filtros da lista de movimentos ganha o botão «Exportar CSV», ao lado
  do contador «N de M», desativado quando não há linhas visíveis.
- O CSV usa o mesmo formato dos relatórios (helper `app/csv.ts`): separador `;`,
  números com vírgula decimal, BOM UTF-8, nome
  `movimentos_{empresa}_{AAAA-MM-DD}.csv`.
- Exporta **o que está no ecrã** — os movimentos que passam a pesquisa, o diário,
  o estado e o intervalo de datas —, uma linha por linha de lançamento, com as
  colunas: Movimento, Data, Diário, Tipo de documento, Documento, Descrição,
  Notas, Estado, Conta, Nome da conta, Descrição da linha, Débito, Crédito.

Sem alteração de esquema nem da API — a fase do `/foundation` mantém-se em F1-56.

## Superfície de validação local

Na lista de movimentos, filtrar (por período, diário, estado ou pesquisa) e usar
«Exportar CSV»: o ficheiro traz só as linhas dos movimentos visíveis.

## Validação efetuada

- `NexGenAccountingAI.Web`: lint, build de produção e trinta e três testes de interface
  (o teste do filtro de datas passa também a exercitar o «Exportar CSV»,
  confirmando que gera um _blob_ e dispara o descarregamento);
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos;
- verificação no browser contra a base `accountingAI`: na empresa de
  demonstração, «Exportar CSV» sem filtros gera 27 linhas (cabeçalho + 26 linhas
  de lançamento dos 9 movimentos), com cabeçalho correto, separador `;` e valores
  como `50,00`; com o intervalo 2026-08-01 a 2026-08-31 («3 de 9») o CSV traz só
  os movimentos `COMP-2026-00842`, `VEND-2026-00327` e `BANC-2026-00196`.

## Fora deste incremento

- exportar em Excel (`.xlsx`) ou PDF;
- uma linha de totais no fim (o livro diário tem-na porque traz totais do
  período; a lista de movimentos mostra valores por lançamento).
