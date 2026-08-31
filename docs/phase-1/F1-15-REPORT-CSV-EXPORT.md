# F1-15 — Exportação CSV dos relatórios contabilísticos

## Objetivo

Permitir levar o livro diário, o balancete e a razão de conta para fora da
aplicação — para arquivo, conferência ou importação noutro programa — sem
depender de copiar do ecrã.

## Regras

Cada um dos três ecrãs ganha um botão **Exportar CSV** que descarrega exatamente
o que está a ser mostrado, com os filtros em vigor (empresa, diário, tipo de
documento, exercício, intervalo de datas e, no balancete, a pesquisa de texto).

O ficheiro usa `;` como separador, vírgula decimal e duas casas nos valores,
prefixo BOM UTF-8 (para o Excel apresentar os acentos) e o nome
`{relatório}_{empresa}_{AAAA-MM-DD}.csv`. O livro diário exporta uma linha por
linha de lançamento e termina com a linha de totais; a razão começa pela linha de
saldo de abertura quando há um intervalo definido.

Toda a lógica vive no cliente (`src/app/csv.ts`); não há endpoint novo nem
alteração de domínio, infraestrutura ou API.

## Superfície de validação local

Nos ecrãs **Livro diário**, **Balancete** e **Razão da conta**, o botão
`Exportar CSV` na barra de filtros. Fica desativado quando não há linhas para
exportar.

## Validação efetuada

- testes unitários de `toCsv`/`csvFilename` (separador, vírgula decimal, escape de aspas e `;`, remoção de acentos no nome do ficheiro);
- teste de interface: no livro diário, clicar em `Exportar CSV` cria um object URL e aciona a transferência uma vez;
- lint, vinte e um testes de interface (dois ficheiros) e build de produção da PWA;
- verificação no browser: o botão aparece nos três ecrãs e o clique não gera erros de consola;
- suite .NET completa (54 testes), `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências.

## Fora deste incremento

- exportação no servidor (para volumes grandes) e outros formatos (XLSX, PDF);
- exportação do plano de contas e das taxonomias;
- saldo de abertura por conta no balancete;
- séries por tipo de documento e exercício com contadores persistidos.
