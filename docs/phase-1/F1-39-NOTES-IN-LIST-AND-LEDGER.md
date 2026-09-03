# F1-39 — Observações visíveis na lista e na razão

## Objetivo

Aproveitar a nota interna do F1-38 fora do editor: assinalá-la onde os
lançamentos se consultam — a lista de movimentos e a razão de conta — e deixar
pesquisar por ela.

## Regras

`AccountLedgerEntry` ganha `Notes` (`string?`); `AccountLedgerService` projeta
`JournalEntry.Notes` em cada linha da razão e o endpoint
`GET .../accounts/{code}/ledger` devolve `notes` por entrada.

Na PWA:

- a lista de movimentos passa a incluir o texto da nota no filtro de pesquisa
  (a par da referência, descrição e documento);
- as linhas com nota — na lista de movimentos e na razão de conta — mostram uma
  etiqueta **nota** (`title` com o texto completo).

Sem alteração de esquema.

## Superfície de validação local

`GET .../accounting/accounts/{code}/ledger` devolve `notes` em cada entrada. Nos
ecrãs **Movimentos** e **Razão da conta**, as linhas com observação têm a
etiqueta **nota**; a caixa de pesquisa dos movimentos encontra lançamentos pelo
texto da nota.

## Validação efetuada

- build .NET Debug sem avisos nem erros;
- teste de integração SQL Server LocalDB: a entrada da razão de `GERAL-2026-00002` traz `Notes = "Corrigido após revisão"`;
- teste de API: `/foundation` indica a fase `F1-39`; o corpo da razão inclui `"notes"`;
- verificação no browser contra a base `accountingAI`: criado `GERAL-2027-00002` com nota, a etiqueta **nota** aparece na lista e na razão de `6221` com o texto no `title`, e a pesquisa «classificacao» filtra para esse lançamento; lançamento anulado no fim;
- teste de interface: a lista mostra a etiqueta **nota**, a pesquisa pelo texto da nota encontra o lançamento e a razão de `6221` mostra a etiqueta com o `title` da nota de demonstração;
- suite .NET completa (59 testes), `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos;
- lint, vinte e oito testes de interface e build de produção da PWA.

## Fora deste incremento

- mostrar a nota no livro diário e nos relatórios;
- editar a nota a partir da lista sem abrir o editor completo;
- histórico/autoria das alterações à nota.
