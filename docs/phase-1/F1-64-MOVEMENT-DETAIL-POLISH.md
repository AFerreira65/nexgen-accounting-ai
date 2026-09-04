# F1-64 — Detalhe do movimento: origem real, tipo de documento, copiar ligação

## Objetivo

O ecrã de detalhe de um movimento mostrava «Origem: Dados de teste locais» (texto
fixo), tinha um botão «Copiar ligação» sem efeito e um «Mais opções» inútil, e o
distintivo de estado ficava amarelo mesmo para os anulados. Agora que os
lançamentos têm origem (F1-63), o detalhe passa a mostrá-la.

## Regras

- A tira de metadados mostra a **origem real** do lançamento
  (`entrySourceLabel(movement.source)` — Manual / Importado do SAF-T / Abertura /
  Apuramento) e o número do documento.
- O cabeçalho mostra o **tipo de documento** (quando existe) ao lado do estado.
- O distintivo de estado usa `neutral` para os anulados, `success` para os
  conferidos e `warning` para os por conferir.
- «Copiar ligação» copia o endereço atual para a área de transferência e confirma
  («Ligação copiada») durante dois segundos; o botão «Mais opções» (sem efeito)
  foi removido.

Sem alteração de esquema nem da API — a fase do `/foundation` mantém-se em F1-63.

## Superfície de validação local

Abrir um movimento: a origem, o tipo de documento e o estado aparecem corretos;
«Copiar ligação» copia o URL.

## Validação efetuada

- `AccountingAI.Web`: lint, build de produção e trinta e seis testes de interface
  (o teste do detalhe passa também a confirmar que a origem mostra «Manual» e não
  o texto fixo antigo, e que o botão «Copiar ligação» está presente);
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos;
- verificação no browser contra a base `accountingAI`: em `AB-2026-00001` o
  detalhe mostra o distintivo `AB` e «Origem: Abertura»; num lançamento manual
  anulado o distintivo de estado fica `neutral` e a origem mostra «Manual».

## Fora deste incremento

- ações (conferir/reabrir/corrigir/anular/duplicar) no ecrã de detalhe;
- abrir o documento digitalizado a partir do cartão «Documento de origem».
