# F1-72 — Filtro de período na visão geral

## Objetivo

Os cartões de posição financeira e o resultado do painel de trabalho só
mostravam a posição atual (todos os movimentos). F1-67 e F1-69 deixaram
isso como próximo passo: dar ao utilizador uma data de referência para ver
como estava a posição num momento anterior, sem sair do dashboard.

## Regras

- O dashboard ganha uma única data de referência («Até», com os mesmos
  atalhos de período — mês, trimestre, ano — dos outros ecrãs). O saldo de
  cada conta usado nos indicadores é sempre cumulativo até essa data (o
  mesmo endpoint `/accounting/trial-balance` que o balancete já usa; o
  saldo de uma conta não depende de onde começa o período, só de onde
  acaba, por isso não há um segundo campo de data inicial).
- Afeta **Resultado**, **Total do ativo**, **Caixa e bancos**, **Saldo de
  clientes**, **Saldo de fornecedores** e **Posição de IVA** — cartões cuja
  legenda passa a indicar «Até {data}» em vez do texto habitual. Fica de
  fora **Movimentos** e **Por conferir**, que são contagens do estado atual
  da fila de trabalho, não uma posição numa data.
- Sem data de referência (por omissão), o comportamento é exatamente o de
  antes.

Sem alteração de esquema nem de API — reutiliza o endpoint do balancete.
Sem alteração de fase (`/foundation` mantém-se em **F1-71**).

## Superfície de validação local

No painel de trabalho, escolher uma data de referência anterior aos
movimentos mais recentes deixa os cartões de posição financeira e o
resultado refletirem só o que existia até essa data.

## Validação efetuada

- `AccountingAI.Web`: lint, build e trinta e oito testes de interface (o
  teste do painel de trabalho passa também pelo filtro, confirmando que
  a posição de IVA muda ao recuar a data de referência antes de um dos
  dois lançamentos de IVA sintéticos, e volta ao valor original ao limpar
  o filtro);
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos.

## Fora deste incremento

- aplicar o mesmo filtro aos indicadores de Movimentos e Por conferir;
- guardar a data de referência escolhida entre visitas ao dashboard.
