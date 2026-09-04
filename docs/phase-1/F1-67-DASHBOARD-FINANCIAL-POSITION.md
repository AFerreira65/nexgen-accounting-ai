# F1-67 — Posição financeira na visão geral

## Objetivo

A visão geral mostrava movimentos, resultado, total do ativo e pendentes. Faltava
o mais consultado no dia a dia: caixa/bancos e os saldos de clientes e
fornecedores.

## Regras

- A visão geral ganha uma segunda fila de indicadores «Posição financeira»,
  calculada sobre o balancete que a página já lê:
  - **Caixa e bancos** — soma das contas cujo código começa por `11` ou `12`;
  - **Saldo de clientes** — soma das contas que começam por `21`;
  - **Saldo de fornecedores** — soma das contas que começam por `22`.
- Cada cartão é uma ligação: caixa/bancos para o balancete, clientes e
  fornecedores para as contas correntes.

Sem alteração de esquema nem da API — a fase do `/foundation` mantém-se em F1-66.

## Superfície de validação local

Na visão geral, os três cartões de posição financeira mostram os saldos e abrem o
ecrã respetivo.

## Validação efetuada

- `AccountingAI.Web`: lint, build e trinta e seis testes de interface (o teste da
  visão geral confirma os três novos cartões);
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos;
- verificação no browser contra a base `accountingAI`: os cartões mostram «Caixa e
  bancos» (contas 11 e 12), «Saldo de clientes» 6150,00 € e «Saldo de
  fornecedores» -2000,00 € — a bater certo com o ecrã de contas correntes —, e o
  cartão de clientes abre `/contas-correntes`.

## Fora deste incremento

- indicador da posição de IVA na visão geral;
- filtrar os indicadores por período.
