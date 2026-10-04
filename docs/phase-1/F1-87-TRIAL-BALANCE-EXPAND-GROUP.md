# F1-87 — Balancete agrupado: expandir uma classe ou rubrica

## Objetivo

F1-79 fez o balancete agrupado (por classe ou taxonomia) mostrar só os
subtotais quando há contas a mais, mas não havia forma de ver as contas de
uma classe sem desligar o agrupamento e perder o contexto.

## Regras

- Quando o balancete agrupado está colapsado (contas acima do tamanho de
  página), cada cabeçalho de grupo ganha um «ver contas» / «ocultar».
- Expandir um grupo mostra as suas contas, paginadas dentro do grupo pelo
  tamanho de página da empresa, com navegação Anterior/Seguinte própria.
- Mudar de agrupamento ou de filtro (pesquisa, datas, homólogo) volta a
  colapsar tudo.
- Os subtotais e os totais continuam a ser do grupo/balancete inteiro.
- As duas variantes de agrupamento (classe e taxonomia) passam a partilhar
  um `renderGroup` — deixa de haver dois blocos quase iguais.

Sem alteração de esquema nem de API — puramente do lado do cliente. A fase
do `/foundation` mantém-se em F1-71.

## Superfície de validação local

Num balancete agrupado e colapsado, expandir uma classe mostra as suas
contas paginadas; recolher volta a esconder.

## Validação efetuada

- `NexGenAccountingAI.Web`: lint, build e quarenta e seis testes de interface (o
  teste do balancete agrupado continua válido — a empresa de demonstração
  tem poucas contas, por isso mostra-as sempre sem o botão);
- `dotnet build`/`dotnet test` na solução inteira sem falhas;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação no browser contra a base `accountingAI`, na empresa Vidreira
  Algarvia: no balancete agrupado por classe, «ver contas» na classe 6
  (124 contas) mostra-as paginadas dentro do grupo; recolher esconde.

## Fora deste incremento

- fazer o mesmo no Balanço (F1-84 remete para o balancete, que agora é
  drillável).
