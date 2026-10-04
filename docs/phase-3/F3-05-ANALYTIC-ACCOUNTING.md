# F3-05 — Contabilidade analítica

Quinto e último épico do Bloco A da [Fase 3](README.md). Todos os programas
comparados têm centros de custo; aqui a única forma de separar uma obra ou um
departamento era desdobrar o plano de contas.

## Desenho

Uma **dimensão analítica na linha do lançamento**, à imagem do terceiro e do
código de IVA: opcional, aditiva, e sem tocar nos lançamentos que já existem.
Não se cria uma classe 9 nem uma contabilidade paralela — o centro de custo
acompanha a linha da contabilidade geral.

## Sub-incrementos

### F3-05A — Centros de custo, linha e mapa analítico (feito)

- **`Domain/Accounting/CostCenter`** — `Code` (até 20; letras, algarismos,
  pontos e hífenes; único por empresa), `Name`, `IsActive`.
- **`accounting.CostCenters`** e **`JournalEntryLines.CostCenterId`** (coluna
  anulável, FK sem cascata) — migração aditiva `20261003202303_CostCenters`;
  auditável, no `record-info` (`centros-custo`) e no dicionário de dados.
- **`JournalPostingService`** — `JournalEntryLineInput.CostCenter` (o código);
  um centro desconhecido ou inativo recusa o lançamento antes de gerar a
  referência. `PostedMovementLine.CostCenter` devolve-o.
- **`CostCenterService`** — `ListAsync`, `CreateAsync`, `UpdateAsync` (nome e
  estado) e `ReportAsync`, o **mapa analítico**:
  - só as contas de **gastos e rendimentos** (classes 6 e 7), sem os
    lançamentos anulados, no período pedido;
  - por centro: gastos (débito − crédito da classe 6), rendimentos (crédito −
    débito da classe 7), resultado, e as contas que o compõem;
  - as linhas **sem centro** entram numa linha própria, no fim, para o total do
    mapa bater com a demonstração de resultados.
- **API:** `GET .../cost-centers?includeInactive=`, `POST .../cost-centers`,
  `PUT .../cost-centers/{código}`, `GET .../cost-centers/report?from=&to=`; e
  `costCenter` nas linhas de `POST`/`PUT .../accounting/movements` e nas
  leituras de movimentos.
- **PWA:**
  - ecrã **«Centros de custo»** (`/centros-custo`, grupo «Cadastros»);
  - ecrã **«Mapa analítico»** (`/mapa-analitico`, grupo «Mapas») — período
    com os atalhos habituais, totais, uma linha por centro que se expande para
    as contas, e CSV;
  - no **editor de lançamentos**, a coluna **«C. custo»** — só aparece quando
    a empresa tem centros de custo (ou o lançamento já os traz), para não
    ocupar espaço a quem não usa analítica; o detalhe do movimento mostra-o.

**Validação efetuada**

- `NexGenAccountingAI.Domain.Tests`: normalização do código e a linha com centro.
- `NexGenAccountingAI.IntegrationTests`: `CostCenterServiceTests` — cadastro
  (duplicado, código inválido, inativo), lançamentos com centro, centro
  desconhecido e inativo recusados, e o mapa: três centros com a linha «sem
  centro» no fim, sem o lançamento anulado, com gastos 370, rendimentos 1000 e
  resultado 630; fora do período vazio. `SqlServerMigrationTests` inclui a
  migração.
- `NexGenAccountingAI.Api.Tests`: `CostCentersListReportAndValidateInput`.
- `NexGenAccountingAI.Web`: `tsc`, `eslint`, os 90 testes; os dois ecrãs foram
  abertos no navegador em modo de demonstração, e confirmou-se que o editor
  não mostra a coluna quando não há centros.
- A suite completa: 227 testes .NET.
- **Por verificar em direto:** criar centros, lançar com eles e abrir o mapa
  contra a base real.

**Fora deste incremento**

- **Repartição de uma linha por vários centros** (chaves de rateio): hoje uma
  linha tem um só centro; reparte-se desdobrando a linha.
- **Hierarquia de centros** e uma segunda dimensão (projeto, departamento).
- **Centro de custo nos lançamentos-tipo** e a sua proposta por conta.
- **Orçamento por centro e desvios** — F3-24.
- A importação do SAF-T não traz centros de custo: o ficheiro não os tem.
