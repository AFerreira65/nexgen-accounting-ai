# F1-32 — Indicadores contabilísticos no dashboard

## Objetivo

Transformar a visão geral num painel útil: além da contagem de movimentos, mostrar
o resultado e o ativo da empresa ativa.

## Regras

O dashboard passa a consultar o balancete da empresa (`/accounting/trial-balance`,
sem intervalo de datas) e a derivar dele:

- **Resultado** — `−(soma dos saldos das classes 6 e 7)`; positivo é lucro
  acumulado, negativo é prejuízo;
- **Total do ativo** — soma dos saldos das classes 1, 3, 4 e das contas de classe
  2 com saldo devedor.

Os quatro cartões passam a ser **Movimentos**, **Resultado**, **Total do ativo** e
**Por conferir**. Sem ligação ao SQL Server, os dois novos indicadores são
calculados a partir dos movimentos em memória.

Alteração apenas no cliente (`DashboardPage.tsx`); reutiliza o endpoint do
balancete. Sem alteração de API, domínio nem esquema.

## Validação efetuada

- lint, vinte e sete testes de interface (o teste do dashboard confirma os cartões «Resultado» e «Total do ativo») e build de produção da PWA;
- verificação no browser contra a API: com o exercício de 2026 já apurado, o cartão «Resultado» mostra 0,00 € (as classes 6 e 7 estão a zero) e «Total do ativo» 5750,00 €;
- suite .NET completa (56 testes), `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências.

## Fora deste incremento

- indicadores por período (o dashboard usa o acumulado);
- gráfico de evolução do resultado;
- alertas de contas sem taxonomia ou de séries por iniciar.
