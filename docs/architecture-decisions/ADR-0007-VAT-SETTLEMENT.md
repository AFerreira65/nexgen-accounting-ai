# ADR-0007 — Apuramento periódico de IVA a partir do razão

- Estado: proposto para a F2-08
- Data: 2026-09-09

## Contexto

O ponto 3 da visão pede a comunicação direta com a AT para o IVA periódico, a
IES e o IRC. A comunicação em si precisa de certificação e credenciais de teste
da AT — está bloqueada externamente. Mas o **cálculo** de cada obrigação a
partir da contabilidade não está, e é a maior parte do valor: é o que um
contabilista faz todos os meses ou trimestres antes de submeter.

O primeiro passo é o **apuramento periódico de IVA**: a partir dos movimentos
das contas 24.3 num período, determinar o IVA liquidado, o dedutível, as
regularizações, o excesso reportado do período anterior, e a posição final
(a entregar ao Estado ou a reportar).

O risco é misturar isto com a declaração periódica oficial da AT (os campos
numerados do modelo em vigor). O mapeamento das contas para esses campos não é
determinístico sem uma configuração de IVA por conta (taxa, tipo de operação)
que a plataforma ainda não tem, e apresentar um número de campo errado seria
pior do que não o mostrar.

## Decisão

- A F2-08 produz o **apuramento contabilístico** de IVA, não a declaração
  periódica da AT. É o movimento das contas 24.3 no período, com a aritmética do
  fecho de IVA:
  - **IVA liquidado** = Σ (crédito − débito) nas contas `2433*` do período;
  - **IVA dedutível** = Σ (débito − crédito) nas contas `2432*` do período;
  - **regularizações a favor do Estado** = Σ crédito nas `2434*`;
    **a favor do sujeito passivo** = Σ débito nas `2434*` (em bruto, não
    compensadas);
  - **excesso a reportar do período anterior** = saldo devedor das contas
    `2437*` à data anterior ao início do período — assumido do razão, com a
    marca `PriorCreditAssumed`, até F2-08C o tornar um valor explícito e
    encadeado entre apuramentos;
  - **resultado do período** = (liquidado + regularizações a favor do Estado) −
    (dedutível + regularizações a favor do sujeito passivo);
  - **a entregar** = resultado − excesso anterior, quando positivo;
    **excesso a reportar** = o simétrico, quando negativo.
- Há uma **desagregação por conta** (todas as `243*` que se moveram no período,
  com débito/crédito/saldo e o papel de cada uma) para a rastreabilidade até ao
  lançamento.
- `IVatReturnService` (fronteira em `Application`), `VatReturnService`
  (`Infrastructure.SqlServer`). Só lê; exclui os anulados. Devolve `null` sem
  acesso à empresa.
- **`accounting.VatReturn`** (F2-08C) guarda um apuramento por
  `(empresa, período)` — o valor do excesso anterior (já como input do
  utilizador), a fotografia calculada, e o estado (rascunho). Auditável
  (F2-06A). O período seguinte propõe como excesso anterior o excesso a reportar
  do apuramento anterior.
- A folha impressa reaproveita o `ReportChrome` (chave `apuramento-iva`).
- **Fora do âmbito:** a submissão à AT (certificação, webservice), os campos
  numerados do modelo oficial (precisam de configuração de IVA por conta), e as
  restantes obrigações (IES, IRC) — cada uma um épico próprio quando houver
  credenciais e/ou a configuração de suporte.

## Consequências

- O contabilista tem o apuramento de IVA do período com um clique, ancorado nas
  contas e nos lançamentos, e um histórico com o encadeamento do excesso a
  reportar — o núcleo do trabalho periódico, sem depender da AT.
- O `Resumo de IVA` (F1-61) continua a ser a leitura simples das contas 24.3;
  o apuramento acrescenta as regularizações, o reporte do período anterior, a
  posição final e a persistência.
- Quando houver a configuração de IVA por conta e as credenciais da AT, a
  declaração periódica oficial e a sua submissão assentam neste apuramento em
  vez de recomeçarem do zero.
