# F1-33 — Largura configurável da numeração

## Objetivo

Deixar de fixar a numeração dos lançamentos em cinco dígitos: cada série e
exercício pode escolher a largura de preenchimento da referência.

## Regras

`DocumentNumberSeries` ganha `Width` (1 a 12, por omissão 5) e o método
`Format(number)` que produz `{SÉRIE}-{ANO}-{N}` com o número preenchido a zeros
até essa largura (sem truncar números maiores). `JournalPostingService` passa a
usar `series.Format(...)` em vez do formato fixo `D5`.

`DocumentNumberSeriesService.AdvanceAsync` aceita agora uma largura opcional; ao
avançar o contador de uma série pode-se também alterar a largura (validada entre
1 e 12). O estado devolvido inclui `Width`. Sem contador persistido, a largura
apresentada é 5.

## Alteração de esquema

Migração `20260903124046_DocumentNumberSeriesWidth`: coluna `Width int NOT NULL
DEFAULT 5` em `accounting.DocumentNumberSeries` (não destrutiva; as linhas
existentes ficam a 5). Aplicada à base `accountingAI`.

## Superfície de validação local

O `PUT .../accounting/number-series/{serie}/{ano}` aceita `{ "nextNumber": N,
"width": W }`. No ecrã **Séries**, a nova coluna **Largura** mostra a largura e
uma pré-visualização da próxima referência; o editor de cada série passa a ter um
campo de largura.

## Validação efetuada

- build .NET Release sem avisos nem erros;
- teste de domínio: `Format` preenche até à largura e não trunca, `ChangeWidth` valida o intervalo 1–12;
- teste de integração SQL Server LocalDB: a migração consta das aplicadas; `AdvanceAsync(..., width: 6)` guarda a largura; o lançamento `FTV` seguinte fica com a referência `FTV-2026-000010`; uma largura fora do intervalo é recusada;
- teste de API: `/foundation` indica a fase `F1-33`;
- verificação: `GET .../number-series` devolve `"width"` em cada série;
- suite .NET completa (57 testes), `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências;
- lint, vinte e sete testes de interface e build de produção da PWA.

## Fora deste incremento

- prefixo/máscara de texto configurável além da largura;
- largura por defeito ao provisionar a empresa;
- renumeração retroativa de referências já emitidas.
