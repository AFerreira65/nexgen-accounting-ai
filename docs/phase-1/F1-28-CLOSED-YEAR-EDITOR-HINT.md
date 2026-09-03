# F1-28 — Aviso de exercício apurado no editor de movimentos

## Objetivo

Trazer o bloqueio de F1-26/F1-27 para a frente do utilizador: no editor de
movimentos, avisar antes de submeter quando a data escolhida cai num exercício
já apurado.

## Regras

Novo endpoint `GET .../accounting/closed-years` devolve a lista de exercícios da
empresa com um apuramento (`AP`) não anulado (`YearEndClosingService.ClosedYearsAsync`).

O editor de movimentos consulta esta lista ao abrir (só com ligação ao SQL
Server). Se o ano da data do lançamento estiver na lista, mostra o aviso «O
exercício de {ano} está apurado. Reabra-o para lançar com esta data.» e desativa
o botão de registo/correção. A API mantém a verificação como última linha de
defesa.

Sem alteração de esquema.

## Validação efetuada

- build .NET Release sem avisos nem erros;
- teste de API: `/foundation` indica a fase `F1-28`; `GET .../closed-years` devolve `200` e um array JSON;
- verificação no browser contra a API (`closed-years` = `[2026]`): ao escolher uma data de 2026 no editor, aparece o aviso e o botão fica desativado; ao mudar para 2027, o aviso desaparece;
- suite .NET completa (56 testes), `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências;
- lint, vinte e sete testes de interface e build de produção da PWA.

## Fora deste incremento

- aviso equivalente no ecrã de correção a partir da lista de movimentos;
- indicação do exercício apurado no seletor de data.
