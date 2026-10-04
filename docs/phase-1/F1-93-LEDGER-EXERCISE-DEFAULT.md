# F1-93 — Razão de conta arranca no exercício

## Objetivo

Depois de a lista de movimentos passar a arrancar no exercício da empresa
(F1-92), a razão de conta ficava a destoar — abria com toda a história da
conta e sem linha de saldo de abertura.

## Regras

- A razão arranca com o intervalo de datas no exercício da empresa,
  dando a leitura habitual «saldo de abertura → movimentos → saldo final».
- Volta a acertar ao mudar de conta, de empresa ou de exercício.
- Novo botão «Limpar datas» abre a conta inteira; os atalhos de período e
  as datas manuais funcionam como antes.
- Sem exercício definido, arranca sem filtro (como antes).
- O cartão de saldo, sem período, passa a dizer «Todas as datas» em vez de
  «Exercício de {ano}» — só se lá chega depois de «Limpar datas».

Sem alteração de esquema nem de API — puramente do lado do cliente. A fase
do `/foundation` mantém-se em F1-71.

## Superfície de validação local

Abrir a razão de uma conta mostra o exercício da empresa por omissão, com
a linha de saldo de abertura e «Limpar datas» à vista.

## Validação efetuada

- `NexGenAccountingAI.Web`: lint, build e cinquenta testes de interface (o teste
  da razão passa a confirmar o arranque em 2026-01-01 e «Limpar datas»);
- `dotnet build`/`dotnet test` na solução inteira sem falhas;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação no browser contra a base `accountingAI`, na empresa Vidreira
  Algarvia: a razão da conta 21 abre filtrada a 2025 com o saldo de
  abertura; «Limpar datas» mostra a conta inteira.

## Fora deste incremento

- lembrar entre visitas a escolha manual de datas do utilizador.
