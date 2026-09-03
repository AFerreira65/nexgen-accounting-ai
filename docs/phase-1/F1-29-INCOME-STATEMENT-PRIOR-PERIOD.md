# F1-29 — Comparativo com o período homólogo na demonstração de resultados

## Objetivo

Dar à demonstração de resultados a leitura que os contabilistas fazem sempre:
período atual contra o período homólogo do exercício anterior.

## Regras

No ecrã **Demonstração de resultados**, um interruptor **Comparar com período
homólogo** — disponível quando estão preenchidas a data inicial e a data final —
carrega um segundo balancete para o mesmo intervalo recuado exatamente um ano.

Com a comparação ligada, cada subclasse e cada total ganha duas colunas:
**Homólogo** (o montante do período anterior) e **Variação** (atual − homólogo). A
comparação é ao nível da subclasse; as linhas de conta não repetem o homólogo. Os
cartões do topo passam a mostrar o valor homólogo, e a exportação CSV inclui as
duas colunas.

Alteração apenas no cliente (`IncomeStatementPage.tsx`); reutiliza o endpoint do
balancete. Sem alteração de API, domínio nem esquema.

## Validação efetuada

- lint, vinte e sete testes de interface (o teste da demonstração de resultados liga a comparação e confirma a coluna «Homólogo») e build de produção da PWA;
- verificação no browser contra a API: com o intervalo `2026-01-01`–`2026-12-30` e a comparação ligada, a subclasse `72` mostra 2500 € / homólogo 0 € / variação 2500 €, e os totais e o resultado seguem a mesma leitura;
- suite .NET completa (56 testes), `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências.

## Fora deste incremento

- comparativo homólogo no balancete e no balanço;
- comparação ao nível da conta de movimento;
- escolha livre do período de comparação (não apenas o ano anterior).
