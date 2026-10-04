# F1-69 — Posição de IVA na visão geral

## Objetivo

F1-67 trouxe caixa/bancos e os saldos de clientes/fornecedores para a visão
geral; faltava a posição de IVA acumulada, que hoje só se via no ecrã dedicado.

## Regras

- A fila «Posição financeira» ganha um quarto cartão, **Posição de IVA**, com a
  mesma fórmula do ecrã «Resumo de IVA» (F1-61): soma de `crédito − débito` das
  contas `2433*` (liquidado) menos soma de `débito − crédito` das `2432*`
  (dedutível). Positiva = «A entregar ao Estado»; negativa = «A recuperar ou a
  reportar»; nula = «Nulo».
- O cartão liga ao ecrã `/iva`.

Sem alteração de esquema nem da API — a fase do `/foundation` mantém-se em F1-66.

## Superfície de validação local

Na visão geral, o cartão «Posição de IVA» mostra o mesmo valor que o ecrã «Resumo
de IVA» (sem filtro de datas) e abre-o.

## Validação efetuada

- `NexGenAccountingAI.Web`: lint, build e trinta e sete testes de interface (o teste da
  visão geral confirma o cartão «Posição de IVA» com 299,00 € «A entregar ao
  Estado» na empresa de demonstração);
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos;
- verificação no browser contra a base `accountingAI`: na empresa da ilha, o
  cartão mostra 160,00 € «A entregar ao Estado», exatamente o valor do ecrã
  «Resumo de IVA» sem filtro de datas, e abre `/iva` ao clicar.

## Fora deste incremento

- filtrar os indicadores da visão geral por período.
