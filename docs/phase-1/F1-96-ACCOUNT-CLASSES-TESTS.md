# F1-96 — Testes do agrupamento por classe e subclasse

## Objetivo

`src/app/accountClasses.ts` traduz os códigos SNC em nomes de classe e
subclasse e, sobretudo, `groupBySubclass` soma as contas de movimento por
subclasse de dois dígitos — é dele que o Balanço (F1-84) e a demonstração
de resultados tiram os subtotais. Um engano aqui falseava os totais das
demonstrações em silêncio, e o ficheiro não tinha nenhum teste.

## Regras

- Novo ficheiro `src/app/accountClasses.test.ts`, só sobre as funções
  puras (sem alteração de comportamento):
  - `classNameOf` — nome da classe pelo primeiro dígito, com corte de
    espaços e recurso a «Outras contas» para um código fora da tabela;
  - `subclassNameOf` — nome da subclasse pelos dois primeiros dígitos,
    com recurso a «Subclasse XX» quando não é conhecida;
  - `groupBySubclass` — agrupa pelos dois dígitos e soma cada grupo,
    descarta as linhas com montante zero, mantém montantes negativos
    (o passivo e o capital próprio já vêm com o sinal trocado dos
    ecrãs), ordena grupos e linhas por código, e devolve `total` igual
    à soma dos grupos; lista vazia devolve `{ groups: [], total: 0 }`.

Sem alteração de esquema, API nem comportamento. A fase do `/foundation`
mantém-se em F1-71.

## Superfície de validação local

`npm run test` cobre agora o agrupamento por classe e subclasse.

## Validação efetuada

- `NexGenAccountingAI.Web`: lint, build e setenta e três testes de interface
  (treze novos, unitários, para `accountClasses`);
- `dotnet build`/`dotnet test` na solução inteira sem falhas;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos.

## Fora deste incremento

- teste de renderização do Balanço e da demonstração de resultados a
  partir de movimentos (coberto de forma indireta pelos testes de ecrã
  existentes).
