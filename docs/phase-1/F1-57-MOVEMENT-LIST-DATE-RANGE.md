# F1-57 — Filtro de intervalo de datas na lista de movimentos

## Objetivo

Depois de importar um exercício inteiro de um SAF-T, a lista de movimentos só se
filtrava por pesquisa, diário e estado. Falta o intervalo de datas que os
relatórios já têm.

## Regras

- A barra de filtros da lista de movimentos ganha duas datas (`Data inicial`,
  `Data final`) e os atalhos de período (Mês, Trimestre, Ano) do componente
  partilhado `PeriodShortcuts` (F1-49), com o exercício predefinido da empresa
  como referência.
- O filtro é do lado do cliente, como os outros da lista: um movimento aparece
  quando a sua data está dentro do intervalo (comparação de datas ISO), além de
  cumprir a pesquisa, o diário e o estado.
- Um botão «Limpar datas» aparece quando há datas escolhidas e repõe o intervalo
  vazio.
- O contador «N de M» reflete o resultado com todos os filtros aplicados.

Sem alteração de esquema nem da API — a fase do `/foundation` mantém-se em F1-56.

## Superfície de validação local

Na lista de movimentos, escolher um intervalo de datas (ou um atalho de período)
reduz as linhas às desse período; «Limpar datas» volta a mostrar tudo.

## Validação efetuada

- `NexGenAccountingAI.Web`: lint, build de produção e trinta e três testes de interface
  (um novo: filtrar a lista por `Data final` esconde o movimento de 2026-08-29 e
  mantém o de 2026-08-28, e «Limpar datas» repõe a lista);
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos;
- verificação no browser contra a base `accountingAI`: na empresa de
  demonstração, definir o intervalo 2026-01-01 a 2026-12-31 passa a lista de «9 de
  9» para «5 de 9» (saem os quatro lançamentos de 2027), e «Limpar datas» repõe as
  9 linhas; o grupo «Períodos rápidos» (Mês/Trimestre/Ano) está presente.

## Fora deste incremento

- filtrar por intervalo de datas no servidor (a lista continua a vir toda e a
  filtrar-se no cliente);
- guardar o intervalo escolhido entre visitas à página.
