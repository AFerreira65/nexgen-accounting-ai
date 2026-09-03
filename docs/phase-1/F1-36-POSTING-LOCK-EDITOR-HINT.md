# F1-36 — Aviso de período bloqueado no editor de movimentos

## Objetivo

Trazer o bloqueio de período do F1-35 para a frente do utilizador: no editor de
movimentos, avisar antes de submeter quando a data escolhida cai num período já
bloqueado para lançamentos.

## Regras

Novo endpoint `GET .../accounting/posting-lock-date` devolve `{ "lockDate":
"AAAA-MM-DD" }` (ou `{ "lockDate": null }`) com a data de bloqueio da empresa.

O editor de movimentos consulta esta data ao abrir (só com ligação ao SQL
Server). Se a data do lançamento for anterior ou igual à data de bloqueio,
mostra o aviso «O período até {data} está bloqueado para lançamentos.» e desativa
o botão de registo/correção. O aviso de exercício apurado (F1-28) tem
precedência: quando os dois se aplicam, mostra-se só o do apuramento. A API
mantém a verificação (`EnsurePeriodIsOpenAsync`) como última linha de defesa.

Sem alteração de esquema.

## Validação efetuada

- build .NET Debug sem avisos nem erros;
- teste de API: `/foundation` indica a fase `F1-36`; `GET .../posting-lock-date` devolve `200` com a propriedade `lockDate`, e `404` para uma empresa sem acesso;
- verificação no browser contra a API: com a data de bloqueio da empresa em 2027-06-30, ao escolher 2027-03-01 no editor aparece «O período até 2027-06-30 está bloqueado para lançamentos.» e o botão fica desativado; ao mudar para 2027-09-01 o aviso desaparece;
- suite .NET completa (58 testes), `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos;
- lint, vinte e oito testes de interface e build de produção da PWA.

## Fora deste incremento

- aviso equivalente no ecrã de correção a partir da lista de movimentos;
- indicação do período bloqueado no seletor de data;
- bloqueio automático ao registar o apuramento.
