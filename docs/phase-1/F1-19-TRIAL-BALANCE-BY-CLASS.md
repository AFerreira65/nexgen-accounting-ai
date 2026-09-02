# F1-19 — Balancete por classes com subtotais

## Objetivo

Dar ao balancete a leitura por classe de conta, com subtotais, que é como um
contabilista o revê — sem deixar de ter a lista plana quando ela é mais prática.

## Regras

No ecrã **Balancete**, um interruptor **Agrupar por classe** reorganiza as linhas
por classe SNC (o primeiro dígito do código): 1 Meios financeiros líquidos,
2 Contas a receber e a pagar, 3 Inventários e ativos biológicos, 4 Investimentos,
5 Capital, reservas e resultados transitados, 6 Gastos, 7 Rendimentos,
8 Resultados, 0 Contas extrapatrimoniais; qualquer outro dígito cai em «Outras
contas».

Cada grupo tem um cabeçalho e uma linha de subtotal (abertura, débito, crédito e
saldo somados). O total geral e o equilíbrio não mudam. A pesquisa de texto, o
intervalo de datas e o saldo de abertura continuam a aplicar-se antes do
agrupamento.

A exportação CSV segue o que está no ecrã: agrupada, ganha a coluna «Classe» e
uma linha de subtotal por classe; plana, mantém-se como estava.

Sem alteração de API nem de domínio — o agrupamento é feito no cliente a partir
das linhas que o endpoint do balancete já devolve.

## Superfície de validação local

Ecrã `/balancete`: interruptor «Agrupar por classe» na barra de filtros; com ele
ligado, cabeçalhos «N · Nome da classe» e linhas «Subtotal classe N».

## Validação efetuada

- lint, vinte e dois testes de interface (novo: ligar o agrupamento mostra «6 · Gastos» e «Subtotal classe 6» sem perder a conta 6221; desligar volta à lista plana) e build de produção da PWA;
- verificação no browser contra a API: as quatro classes presentes (1, 2, 6, 7) agrupam as oito contas, cada subtotal bate certo (classe 1: 1000/1476/−476; classe 2: 4827/3051/1776; classe 6: 1200/0/1200; classe 7: 0/2500/−2500) e a soma dos subtotais iguala o total geral de 7027 €;
- suite .NET completa (56 testes), `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências.

## Fora deste incremento

- subtotais por grau de conta (2, 3, … dígitos) e por conta agregadora;
- demonstração dos resultados e balanço a partir das classes;
- exportação em XLSX/PDF.
