# F1-38 — Observações internas no lançamento

## Objetivo

Dar a cada lançamento um campo de texto livre para notas internas do gabinete —
pendências, contexto, quem pediu o quê — sem misturar com a descrição, que é
curta e sai nos relatórios.

## Regras

`JournalEntry` ganha `Notes` (`string?`, `null` quando vazio) e o método
`SetNotes`, além do parâmetro opcional `notes` no construtor. O texto é aparado;
acima de `JournalEntry.NotesMaxLength` (2000) caracteres lança `ArgumentException`.

`JournalPostingService.PostAsync` e `ReviseAsync` recebem `notes` e passam-no ao
lançamento (corrigir é recriar, por isso a nota também é reescrita). `PostedMovement`
devolve `Notes`.

## Alteração de esquema

Migração `20260903134406_JournalEntryNotes`: coluna `Notes nvarchar(2000) NULL`
em `accounting.JournalEntries` (não destrutiva; os lançamentos existentes ficam
sem nota). Aplicada à base `accountingAI`.

## Superfície de validação local

O `POST`/`PUT .../accounting/movements` aceitam `"notes"` no corpo e a listagem
`GET .../accounting/movements` devolve `notes` em cada lançamento. No editor de
movimentos há um campo **Observações** (área de texto, opcional); o ecrã de
detalhe do movimento mostra uma secção **Observações** quando há nota.

## Validação efetuada

- build .NET Debug sem avisos nem erros;
- teste de domínio: `Notes` é aparado, definido, limpo com texto vazio e recusa acima de 2000 caracteres; o construtor aceita `notes`;
- teste de integração SQL Server LocalDB: a migração consta das aplicadas; `PostAsync(..., notes: "  Conferir contra o extrato  ")` guarda `"Conferir contra o extrato"` (projeção e entidade); `ReviseAsync(..., notes: "Corrigido após revisão")` reescreve a nota;
- teste de API: `/foundation` indica a fase `F1-38`; a listagem de movimentos inclui `"notes":`;
- verificação no browser contra a base `accountingAI`: criado `GERAL-2027-00001` com nota via API, o ecrã de detalhe mostra a secção **Observações** com o texto; lançamento anulado no fim;
- teste de interface: no editor em modo demonstração, preencher **Observações** e abrir o detalhe mostra a nota;
- suite .NET completa (59 testes), `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos;
- lint, vinte e oito testes de interface e build de produção da PWA.

## Fora deste incremento

- histórico/autoria das alterações à nota;
- notas nas linhas do lançamento;
- pesquisa de movimentos pelo texto da nota;
- mostrar a nota na lista de movimentos e na razão de conta.
