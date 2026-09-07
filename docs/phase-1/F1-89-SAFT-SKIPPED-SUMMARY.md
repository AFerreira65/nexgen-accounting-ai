# F1-89 — Resumo das transações saltadas na importação SAF-T

## Objetivo

Na pré-visualização/análise dos movimentos de um SAF-T, a lista de
transações saltadas era desenhada linha a linha. Analisar um ficheiro
grande antes de importar o plano de contas (todas as transações saltam por
falta de contas) enchia o ecrã com dezenas de milhares de linhas e
bloqueava o browser — no próprio ecrã de importação.

## Regras

- Até 50 transações saltadas, mantém-se a tabela linha a linha (transação
  + motivo).
- Acima disso, mostra-se um resumo agrupado por motivo — contagem e alguns
  exemplos de `TransactionID` por motivo, por ordem decrescente de
  contagem.
- Vale tanto para a análise («a saltar») como para o resultado da
  importação («saltadas»).

Sem alteração de esquema nem de API — puramente do lado do cliente. A fase
do `/foundation` mantém-se em F1-71.

## Superfície de validação local

Analisar um SAF-T em que muitas transações seriam saltadas mostra um
resumo por motivo (com contagem e exemplos), não uma linha por transação.

## Validação efetuada

- `AccountingAI.Web`: lint, build e quarenta e sete testes de interface (os
  testes da importação SAF-T continuam válidos — poucas transações
  saltadas, tabela linha a linha);
- `dotnet build`/`dotnet test` na solução inteira sem falhas;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação no browser contra a base `accountingAI`: criar uma empresa a
  partir de um SAF-T e analisar os movimentos sem importar o plano de
  contas mostra o resumo agrupado («a conta … não existe na empresa» ×60,
  com exemplos); a empresa de teste foi removida a seguir.

## Fora deste incremento

- exportar a lista completa das transações saltadas.
