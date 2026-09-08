# ADR-0005 — Motor de mapas e análises guiado por catálogo

- Estado: proposto para a F2-07
- Data: 2026-09-08

## Contexto

A visão da plataforma (pontos 5–7) pede impressões e mapas configuráveis pela
própria aplicação, à imagem dos IDU e das «Análises Avançadas» do PHC: o
utilizador escolhe a origem, as colunas, os filtros, os agrupamentos e os
totais, guarda o mapa e volta a corrê-lo, e configura a folha impressa dos
relatórios.

O risco é abrir SQL livre ou um construtor de consultas genérico sobre o
esquema todo — difícil de proteger (multi-empresa numa base partilhada,
ADR-0003), de testar e de manter estável quando o modelo evolui.

## Decisão

- As análises são **guiadas por um catálogo de campos por origem**
  (`AnalysisFieldCatalog`), definido em código. Cada origem
  (`AnalysisSource`: `Movimentos` primeiro, depois `SaldosDeConta`, …) expõe um
  conjunto fixo de campos com chave, rótulo pt-PT, tipo (texto / número / data /
  dinheiro) e as marcas «agrupável» / «agregável». **Nunca há SQL do
  utilizador.**
- Uma análise é uma **`AnalysisDefinition`** — origem + colunas + filtros
  (`campo, operador, valor`) + agrupamentos + ordenação + agregados +
  total geral — serializada para JSON e guardada em `reporting.SavedAnalysis`,
  por empresa, com as colunas de auditoria de F2-06A.
- O **`AnalysisRunner`** (`Infrastructure.SqlServer`, fronteira `IAnalysisRunner`
  em `Application`) traduz a definição numa consulta EF sobre a origem, aplica
  filtros e ordenação no servidor, materializa até um **limite rígido de
  5 000 linhas** (`Truncated` quando excede) e calcula subtotais por grupo e
  total geral em memória. Uma análise é um mapa de conferência — só lê.
- A validação da definição contra o catálogo (campos existem, os agrupamentos
  são de campos agrupáveis, os agregados de campos agregáveis) é feita ao
  gravar **e** ao correr.
- A configuração de impressão é **estrutural, não um canvas**:
  `reporting.ReportTemplate` por `(CompanyId, ReportKey)` guarda linhas de
  cabeçalho (identificação do gabinete e da empresa), o conjunto de colunas
  visíveis e a orientação; a PWA aplica-o à folha `@media print` dos
  relatórios fixos (o padrão de impressão vem de F2-06E).
- A saída para PDF é a do navegador (`window.print()` → «Guardar como PDF»),
  sem dependência nova. O envio por email fica fora do âmbito local (SMTP e
  segredos) — fronteira documentada em F2-07E.
- Novo esquema **`reporting`** (ADR-0002 para as migrações); as escritas são
  testadas na suíte de integração.

## Consequências

- O utilizador ganha mapas configuráveis sem que a plataforma exponha o
  esquema nem SQL; o catálogo é a superfície de contrato e cresce origem a
  origem.
- Há um custo de manutenção: cada campo novo do catálogo precisa de tradução
  para a consulta no runner. O catálogo e o runner ficam lado a lado por
  origem para não divergirem.
- O limite de 5 000 linhas é deliberado: uma análise é um mapa, não uma
  extração de dados. Extrações maiores são um pedido à parte, futuro.
- Reaproveita-se a lógica de `JournalReportService` / `AccountLedgerService`
  nas origens em vez de a duplicar.
- Os relatórios com IA (F2-05) e as comunicações à AT assentam depois neste
  motor em vez de cada um inventar a sua saída.
