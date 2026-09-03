# F1-49 — Atalhos de período nos relatórios

## Objetivo

Evitar escrever as datas à mão nos relatórios: botões rápidos para **Mês**,
**Trimestre** e **Ano**.

## Regras

`src/app/periods.tsx` expõe os cálculos (`monthRange`, `quarterRange`,
`yearRange`) e o componente `PeriodShortcuts`. A data de referência para o mês e o
trimestre é hoje, mas nunca depois do fim do exercício ativo — assim os atalhos
caem sempre dentro do exercício. **Ano** usa o exercício predefinido da empresa
(1 de janeiro a 31 de dezembro).

O componente aparece a seguir aos campos de data no **Balancete**, **Demonstração
de resultados**, **Balanço**, **Livro diário** e **Razão da conta**; ao carregar
num botão, preenche as datas inicial e final.

Sem alteração de esquema nem de backend.

## Superfície de validação local

Em cada relatório, os botões **Mês / Trimestre / Ano** preenchem o intervalo de
datas.

## Validação efetuada

- verificação no browser contra a base `accountingAI` (exercício 2026, hoje 2026-09-03): no Livro diário, **Mês** → 2026-09-01 a 2026-09-30, **Trimestre** → 2026-07-01 a 2026-09-30, **Ano** → 2026-01-01 a 2026-12-31;
- teste de interface: no Balancete, **Ano** preenche 2026-01-01 e 2026-12-31;
- `scripts/build.ps1 -SyntheticOnly` (suite .NET de 65 testes e smoke tests) sem ocorrências e `gitleaks` sem segredos;
- lint, trinta e dois testes de interface e build de produção da PWA.

## Fora deste incremento

- atalhos para o mês/trimestre anterior;
- guardar o último período usado por relatório;
- atalho para o exercício anterior a par da comparação homóloga.
