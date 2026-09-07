# F1-94 — Demonstração de resultados e Resumo de IVA arrancam no exercício

## Objetivo

A demonstração de resultados e o resumo de IVA são relatórios de um
período (o próprio texto diz «do período»), mas abriam sem filtro de datas
— numa empresa importada de SAF-T sem apuramentos, somavam classes 6/7 e
contas 24.3 de todos os anos.

## Regras

- Ambos arrancam com o intervalo de datas no exercício da empresa, com um
  botão «Limpar datas» para abrir tudo.
- O arranque no exercício, o botão «Limpar datas» e o reacerto ao trocar
  de empresa ou de exercício passam para um hook partilhado
  `useExerciseDateRange` (`src/app/periods.tsx`), adotado também pela lista
  de movimentos (F1-92) e pela razão de conta (F1-93) — deixa de haver a
  mesma lógica repetida em cada ecrã.

Sem alteração de esquema nem de API — puramente do lado do cliente. A fase
do `/foundation` mantém-se em F1-71. (O Balanço fica de fora: é uma
fotografia patrimonial e «à data de hoje» é o arranque certo.)

## Superfície de validação local

Abrir a demonstração de resultados ou o resumo de IVA mostra o exercício
da empresa por omissão, com «Limpar datas» à vista.

## Validação efetuada

- `AccountingAI.Web`: lint, build e cinquenta testes de interface (os
  testes da DR e do resumo de IVA passam a confirmar o arranque em
  2026-01-01 e «Limpar datas»);
- `dotnet build`/`dotnet test` na solução inteira sem falhas;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação no browser contra a base `accountingAI`, na empresa Vidreira
  Algarvia: a demonstração de resultados e o resumo de IVA abrem filtrados
  a 2025.

## Fora deste incremento

- lembrar entre visitas a escolha manual de datas do utilizador.
