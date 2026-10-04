# F1-92 — Lista de movimentos arranca no exercício

## Objetivo

A lista de Movimentos abria sem filtro de datas — numa empresa importada
de SAF-T, com anos de histórico (2002-2025 na Vidreira Algarvia), a
primeira página mostrava lançamentos de vários anos misturados. O livro
diário já arranca no exercício da empresa (F1-78); a lista de movimentos
não.

## Regras

- A lista de Movimentos arranca com o intervalo de datas no exercício da
  empresa (1 de janeiro a 31 de dezembro do exercício predefinido).
- Volta a acertar ao trocar de empresa ou de exercício (como o filtro de
  exercício do livro diário).
- «Limpar datas» abre a lista inteira; os atalhos de período e as datas
  manuais funcionam como antes.
- Uma empresa sem exercício definido arranca sem filtro (como antes).

Sem alteração de esquema nem de API — puramente do lado do cliente. A fase
do `/foundation` mantém-se em F1-71.

## Superfície de validação local

Abrir a lista de Movimentos mostra o exercício da empresa por omissão, com
«Limpar datas» à vista para abrir tudo.

## Validação efetuada

- `NexGenAccountingAI.Web`: lint, build e cinquenta testes de interface (um novo:
  a lista arranca em 2026-01-01 a 2026-12-31 e «Limpar datas» abre tudo);
- `dotnet build`/`dotnet test` na solução inteira sem falhas;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação no browser contra a base `accountingAI`, na empresa Vidreira
  Algarvia: a lista abre já filtrada a 2025 (o exercício), «Limpar datas»
  mostra os 27 311 movimentos de todos os anos.

## Fora deste incremento

- lembrar entre visitas a escolha manual de datas do utilizador.
