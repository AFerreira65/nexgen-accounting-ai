# F1-30 — Comparativo com o período homólogo no balancete

## Objetivo

Estender ao balancete a comparação com o período homólogo já disponível na
demonstração de resultados (F1-29).

## Regras

No ecrã **Balancete**, o interruptor **Comparar com período homólogo** —
disponível quando estão preenchidas a data inicial e a data final — carrega um
segundo balancete para o mesmo intervalo recuado um ano.

Com a comparação ligada, aparecem duas colunas: **Saldo homólogo** (o saldo da
conta no período anterior) e **Variação** (saldo atual − saldo homólogo). Funciona
com e sem o agrupamento por classe (nas subclasses, o homólogo é a soma dos
saldos anteriores das contas do grupo) e nos totais. A exportação CSV inclui as
duas colunas.

Alteração apenas no cliente (`TrialBalancePage.tsx`); reutiliza o endpoint do
balancete. Sem alteração de API, domínio nem esquema.

## Validação efetuada

- lint, vinte e sete testes de interface (o teste do balancete liga a comparação com o intervalo de 2026 e confirma a coluna «Saldo homólogo») e build de produção da PWA;
- suite .NET completa (56 testes), `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências.

## Fora deste incremento

- comparativo homólogo no balanço;
- comparação de abertura, débito e crédito (apenas o saldo é comparado);
- escolha livre do período de comparação.
