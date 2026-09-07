# F1-83 — Pesquisa rápida na barra de topo

## Objetivo

O campo de pesquisa da barra de topo era decorativo — `placeholder` e
`Ctrl K` sem nada por trás. Numa empresa com dezenas de milhares de contas
(importadas de um SAF-T), saltar depressa para uma conta, diário ou
movimento faz falta.

## Regras

- Novo componente `GlobalSearch` (`src/app/GlobalSearch.tsx`): a partir de
  dois caracteres, pesquisa nos dados já carregados pelo `WorkContext` —
  contas (por código ou nome, até 5), diários (até 2) e movimentos (por
  referência, descrição ou documento, até 4), no máximo 9 resultados.
- `Ctrl`/`Cmd`+`K` foca o campo; as setas percorrem os resultados; Enter
  salta para o ativo; clicar num resultado salta para ele. Saltar limpa o
  campo e fecha o menu.
- O menu fecha ao clicar fora ou com Escape (hook `useDismiss` de F1-82).
- Sem resultados, mostra «Sem resultados para «…»».

Sem alteração de esquema nem de API — tudo sobre dados já em memória. A
fase do `/foundation` mantém-se em F1-71.

## Superfície de validação local

Escrever no campo da barra de topo mostra contas, diários e movimentos que
correspondem; escolher um salta para o ecrã respetivo.

## Validação efetuada

- `AccountingAI.Web`: lint, build e quarenta e seis testes de interface
  (um novo: escrever «6221» mostra a conta e escolhê-la abre a razão da
  conta 6221, deixando o campo limpo);
- `dotnet build`/`dotnet test` na solução inteira sem falhas;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação no browser contra a base `accountingAI`, na empresa Vidreira
  Algarvia: pesquisar por código e por nome de conta devolve resultados e
  o Ctrl+K foca o campo.

## Fora deste incremento

- pesquisar também tipos de documento, taxonomias e páginas da app;
- pesquisa no servidor para contas/movimentos que não estejam carregados
  na página atual (hoje limita-se ao que o `WorkContext` já tem).
