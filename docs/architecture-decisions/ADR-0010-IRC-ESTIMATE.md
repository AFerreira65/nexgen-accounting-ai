# ADR-0010 — Estimativa de IRC a partir do resultado contabilístico

- Estado: proposto para a F2-10
- Data: 2026-09-11

## Contexto

O ponto 3 da visão pede a comunicação direta com a AT para o IVA periódico, a
IES e o **IRC**, à imagem do OCC/CentralGest. O IVA já tem o apuramento
contabilístico ([F2-08](../phase-2/F2-08-VAT-SETTLEMENT.md)) e a classificação
por conta ([F2-09](../phase-2/F2-09-ACCOUNT-VAT-CLASSIFICATION.md)). Falta o
IRC.

O IRC tem um risco maior de sobre-alegar conformidade do que o IVA: o
apuramento fiscal parte do resultado contabilístico mas exige **correções
fiscais** (acréscimos e deduções: tributações autónomas, donativos, mais/menos
-valias fiscais, benefícios fiscais, derrama municipal e estadual, reporte de
prejuízos fiscais de exercícios anteriores, etc.) — uma lista extensa, que muda
com a lei de cada ano, e que a aplicação **não tem forma de detetar
automaticamente** a partir dos lançamentos. Tentar adivinhar essas correções, o
número dos campos do Modelo 22, ou fixar uma taxa de IRC como facto imutável no
código seria o mesmo risco de sobre-alegar conformidade que a F2-08 e a F2-09
evitaram para o IVA — e aqui é mais grave, porque o IRC tem menos margem de
erro tolerável (uma taxa errada só é óbvia numa auditoria; um campo do Modelo
22 mal mapeado pode passar despercebido).

## Decisão

- A F2-10 dá a **estimativa de IRC**, não o Modelo 22 nem a submissão: parte
  do resultado líquido do período (classes 6/7, o mesmo cálculo da
  Demonstração de Resultados), soma uma lista de **correções fiscais
  introduzidas pelo utilizador** (descrição + valor, livre — a aplicação não
  as sugere nem as valida contra a lei fiscal), e aplica uma **taxa
  configurável pelo utilizador em cada cálculo** (nunca fixada no código como
  «a taxa atual») ao resultado fiscal quando positivo.
- **Sem persistência nem correções pré-configuradas no primeiro
  sub-incremento** (F2-10A) — cada cálculo é um «e se»: período + taxa +
  correções → estimativa, tudo no pedido, nada gravado. É o mesmo faseamento
  do F2-08 (cálculo primeiro, persistência a seguir, se vier a ser pedida).
- **Explicitamente fora do âmbito** (não se avança sem o utilizador pedir e
  confirmar os factos fiscais): o reporte de prejuízos fiscais de exercícios
  anteriores, a derrama municipal/estadual, as tributações autónomas, os
  benefícios fiscais, os pagamentos por conta/adicionais, e os campos
  numerados do Modelo 22. Quando o resultado fiscal é negativo, a estimativa
  fica em 0 (sem coleta) — não modela o prejuízo fiscal a reportar.
- Um aviso fixo acompanha sempre o resultado: é uma estimativa de apoio ao
  cálculo, a confirmar contra o Modelo 22 e a lei em vigor.

## Consequências

- O contabilista tem uma calculadora rápida do IRC estimado de um período,
  ancorada no resultado contabilístico já disponível, sem a aplicação alegar
  saber correções fiscais ou taxas que não lhe compete adivinhar.
- Fica a base (o resultado líquido do período, por norma já auditável via
  F2-06A) para quando a persistência de estimativas ou o mapeamento ao Modelo
  22 forem pedidos — cada um um sub-incremento à parte, o segundo só com a
  confirmação do utilizador sobre a numeração em vigor (mesma cautela da
  [`ADR-0009`](ADR-0009-ACCOUNT-VAT-CLASSIFICATION.md) para o IVA).
