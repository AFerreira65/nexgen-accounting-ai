# F1-75 — Paginação da lista de movimentos

## Objetivo

Ao testar a importação de um SAF-T real (empresa com anos de movimentos por
liquidar — troca de contabilista ou de software é o caso típico), a
lista de Movimentos desenhava as dezenas de milhares de linhas todas de
uma vez: o browser deixava de responder. A correção do tempo limite SQL
(commit anterior, `fix-sql-command-timeout-large-companies`) resolveu o
erro na leitura; este incremento resolve o desenho.

## Regras

- Nova função partilhada `paginate` (`src/app/pagination.ts`): dado um
  conjunto já filtrado, o tamanho de página e a página pedida, devolve só
  essa fatia — a página pedida é sempre travada ao intervalo válido (nunca
  devolve uma fatia vazia só porque um filtro reduziu o total depois de o
  utilizador ter avançado páginas).
- A lista de Movimentos usa-a com o tamanho de página já guardado nas
  preferências da empresa («Linhas por página», em Definições — existia
  desde a fundação mas nunca tinha sido aplicado a nenhuma lista); muda de
  filtro (pesquisa, diário, estado, origem, datas) volta à primeira
  página. A exportação CSV continua a abranger tudo o que corresponde aos
  filtros, não só a página visível.
- O painel «Movimentos recentes» do dashboard já só mostrava um resumo —
  mas desenhava a lista inteira por trás; passa a mostrar só os 8 mais
  recentes, como já fazia o resto do ecrã.

Sem alteração de esquema nem de API — puramente do lado do cliente, sobre
dados já carregados. A fase do `/foundation` mantém-se em F1-71.

## Superfície de validação local

Numa empresa com mais movimentos do que o tamanho de página, a lista de
Movimentos mostra só uma página de cada vez, com navegação Anterior/
Seguinte; o painel «Movimentos recentes» do dashboard nunca cresce além de
8 linhas.

## Validação efetuada

- `NexGenAccountingAI.Web`: lint, build e quarenta e quatro testes de interface
  (seis novos, unitários, para `paginate`: fatia e contagem de páginas
  corretas, página pedida a mais volta à última, página a menos volta à
  primeira, tudo cabe numa página quando a lista é mais pequena que o
  tamanho de página, tamanho de página nulo ou negativo não rebenta, lista
  vazia não rebenta);
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação no browser contra a base `accountingAI`, na empresa Vidreira
  Algarvia (27 311 movimentos, importados de um SAF-T real): a lista deixa
  de tentar desenhar tudo de uma vez.

## Fora deste incremento

- paginar do lado do servidor (a lista continua a carregar a empresa
  inteira de uma vez; só o desenho é que agora é por página) — se uma
  empresa ainda maior tornar isso necessário, é o próximo passo natural;
- aplicar «Linhas por página» a outras listas longas (razão de conta,
  livro diário, balancete).
