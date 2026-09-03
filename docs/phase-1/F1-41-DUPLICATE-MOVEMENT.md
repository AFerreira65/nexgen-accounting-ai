# F1-41 — Duplicar lançamento

## Objetivo

Acelerar o registo de lançamentos repetitivos: criar um novo a partir de um
existente, com o diário, o tipo de documento, a descrição, as observações e as
linhas já preenchidas.

## Regras

Na lista de movimentos, cada linha (exceto os lançamentos de sistema `AP`/`AB`)
ganha a ação **Duplicar**. Abre o editor em modo de criação, semeado a partir do
movimento de origem:

- diário, tipo de documento, descrição, observações e linhas — copiados;
- data — a de hoje;
- documento (referência externa) — vazio;
- estado — por conferir.

O lançamento resultante é novo: recebe a sua própria referência da série, tal
como qualquer registo manual. O editor mostra o título «Novo lançamento a partir
de {referência}».

Sem alteração de esquema nem de backend.

## Superfície de validação local

No ecrã **Movimentos**, o botão **Duplicar** de um lançamento abre o editor
pré-preenchido; ao registar, aparece um novo lançamento com referência própria.

## Validação efetuada

- verificação no browser contra a base `accountingAI`: **Duplicar** em `VEND-2026-00327` abre o editor com o título «Novo lançamento a partir de VEND 2026 00327», diário `VENDAS`, descrição e as três linhas (`2111001`, `7211`, `24331`) copiadas, data de hoje, documento vazio, estado por conferir e o botão «Registar lançamento»;
- teste de interface: **Duplicar** em `COMP-2026-00842` pré-preenche a descrição e as linhas e, ao registar, cria `COMPRAS-2026-00001`;
- `scripts/build.ps1 -SyntheticOnly` (suite .NET de 59 testes e smoke tests) sem ocorrências e `gitleaks` sem segredos;
- lint, vinte e nove testes de interface e build de produção da PWA.

## Fora deste incremento

- duplicar a partir do ecrã de detalhe do movimento;
- inverter os débitos e créditos ao duplicar (lançamento de estorno);
- guardar modelos de lançamento reutilizáveis.
