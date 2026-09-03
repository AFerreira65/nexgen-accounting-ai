# F1-37 — Ações da lista de movimentos cientes do período fechado

## Objetivo

Fechar a última lacuna do rasto de F1-27/F1-28/F1-36: na lista de movimentos, os
botões **Corrigir** e **Anular** de um lançamento cuja data caia num exercício
apurado ou num período bloqueado deixam de parecer disponíveis.

## Regras

O ecrã de Movimentos consulta `GET .../accounting/closed-years` e
`GET .../accounting/posting-lock-date` ao abrir (só com ligação ao SQL Server).
Para cada linha, `blockedReason(movimento)` devolve:

- `Exercício apurado` — o ano da data está na lista de exercícios apurados;
- `Período bloqueado` — a data é anterior ou igual à data de bloqueio da empresa;
- `null` — caso contrário, ou se o tipo de documento for `AP`/`AB` (lançamentos
  de sistema, isentos tal como na API).

Quando há motivo, **Corrigir** e **Anular** ficam desativados (com o motivo no
`title`) e a célula mostra uma etiqueta com o motivo. **Conferir** continua
disponível — marcar como conferido não é impedido pela API. As verificações do
`JournalPostingService` mantêm-se como última linha de defesa.

Sem alteração de esquema nem de backend.

## Validação efetuada

- verificação no browser contra a base `accountingAI` (exercício de 2026 apurado):
  as linhas de 2026 que não são `AP`/`AB` mostram «Exercício apurado» e têm
  Corrigir/Anular desativados; `AP 2026`, `AB 2026` e `AB 2027` mantêm o Anular
  ativo; com a data de bloqueio da empresa em 2027-06-30, um `OD` de 2027-03-15
  passa a mostrar «Período bloqueado» com Corrigir/Anular desativados e Conferir
  ativo; ao remover a data de bloqueio, volta ao normal;
- `scripts/build.ps1 -SyntheticOnly` (suite .NET de 58 testes e smoke tests) sem ocorrências e `gitleaks` sem segredos;
- lint, vinte e oito testes de interface e build de produção da PWA.

## Fora deste incremento

- indicação equivalente no detalhe do movimento e na razão de conta;
- filtro para mostrar só os movimentos ainda editáveis;
- histórico de quem alterou a data de bloqueio.
