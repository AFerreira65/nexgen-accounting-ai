# F1-61 — Resumo de IVA por período

## Objetivo

Preparar a declaração periódica de IVA obrigava a ler o balancete e somar à mão as
contas de IVA. Falta um ecrã que apresente o movimento das contas 24.3 num período
e a posição (a entregar / a recuperar).

## Regras

- Novo ecrã «Resumo de IVA» (`/iva`), na barra lateral entre «Balanço» e
  «Apuramento».
- Usa o balancete do período (`GET .../accounting/trial-balance?from=&to=`), como
  a demonstração de resultados e o balanço; sem ligação ao SQL Server, reproduz o
  cálculo sobre os movimentos sintéticos.
- Considera as contas cujo código começa por `243` (IVA no SNC), com as subcontas
  a herdar o prefixo:
  - **IVA dedutível** = soma de `débito − crédito` das contas `2432*`;
  - **IVA liquidado** = soma de `crédito − débito` das contas `2433*`;
  - **Posição do período** = liquidado − dedutível — «A entregar ao Estado» se
    positiva, «A recuperar ou a reportar» se negativa, «Nulo» se zero.
- Mostra três cartões (liquidado, dedutível, posição), uma tabela com o débito,
  crédito e saldo de cada conta 24.3 (ligadas à razão), atalhos de período (mês,
  trimestre, ano), pesquisa e exportação CSV do que está no ecrã.
- O cabeçalho assinala que é um apoio de leitura e não substitui a declaração
  periódica.

Sem alteração de esquema nem da API — a fase do `/foundation` mantém-se em F1-60.

## Superfície de validação local

No ecrã «Resumo de IVA», escolher um período (ou um atalho) mostra o IVA liquidado
e dedutível desse período e a posição; «Exportar CSV» leva a tabela e os totais.

## Validação efetuada

- `NexGenAccountingAI.Web`: lint, build de produção e trinta e cinco testes de interface
  (um novo: na empresa de demonstração, o resumo mostra `24323` (dedutível 276) e
  `24331` (liquidado 575), a posição de 299 «A entregar ao Estado», e o
  «Exportar CSV» gera o descarregamento);
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos;
- verificação no browser contra a base `accountingAI`: na empresa da ilha o resumo
  mostra `24332` (liquidado 160, dedutível 0, posição 160); na empresa do
  Continente, com o período de 2026, `24323` (dedutível 276) e `24331`
  (liquidado 575), posição 299 «A entregar ao Estado»; o CSV traz o cabeçalho
  `Conta;Descrição;Débito;Crédito;Saldo`, as linhas das contas e três linhas de
  fecho (dedutível, liquidado, posição).

## Fora deste incremento

- separar o IVA por taxa ou por espaço fiscal (Continente/Açores/Madeira);
- mapear para os campos do modelo da declaração periódica;
- tratar as contas de regularização (`2438`) e o apuramento (`2434`) como passos
  automáticos.
