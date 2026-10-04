# F3-04 — Tabelas fiscais por ano

Quarto épico do Bloco A da [Fase 3](README.md). A estimativa de IRC sugeria 21%
como constante e os códigos de IVA por omissão traziam as percentagens
escritas no código do serviço. As taxas mudam por lei e por ano; passam a vir
de um catálogo com a **fonte** de cada valor.

## Princípio

O catálogo **propõe**, nunca impõe: cada valor continua editável onde é usado
(a taxa da estimativa, a percentagem de cada código de IVA). E cada grupo diz
se foi lido no texto oficial ou se vem de fonte secundária.

## Sub-incrementos

### F3-04A — Catálogo, consulta e primeiros usos (feito)

- **`Application/Fiscal/FiscalTableCatalog`** — `For(ano)` devolve a tabela do
  ano; antes de 2024 devolve `null` (não se inventa); depois de 2028 devolve os
  valores de 2028 assinalados como transportados (`CarriedForward`).
- **Conteúdo e fontes** (consultadas em 2026-10-03):

| Grupo | Valores | Fonte |
|---|---|---|
| IRC 2026–2028 | Geral 19% / 18% / 17%; PME 15% até 50 000 € | Texto oficial — CIRC art. 87.º, Lei n.º 64/2025 e disposição transitória |
| IRC 2024–2025 | Geral 21% / 20%; PME 17% / 16% até 50 000 € | Fonte secundária — por confirmar |
| Derrama estadual | 3% (1,5 a 7,5 M€), 5% (7,5 a 35 M€), 9% (acima) | Texto oficial — CIRC art. 87.º-A |
| IVA Continente | 6% / 13% / 23% | Texto oficial — CIVA art. 18.º, n.º 1 |
| IVA Açores e Madeira | 4% / 9% / 16% e 4% / 12% / 22% | Fonte secundária — fixadas pelas Assembleias Regionais |
| Tributações autónomas (2025 em diante) | Não documentadas 50% / 70%; viaturas 8% / 25% / 32% (37 500 € e 45 000 €); híbridas plug-in 2,5% / 7,5% / 15%; representação 10%; ajudas de custo 5%; agravamento de 10 p.p. com prejuízo | Texto oficial — CIRC art. 88.º |

- **Cálculos de apoio** na própria tabela: `IrcOn(matériaColetável, pme)` — a
  taxa reduzida só na primeira parcela de uma PME — e
  `StateSurtaxOn(lucroTributável)` — por parcelas.
- **API:** `GET /api/v1/development/fiscal-tables/{ano}` (`404` para um ano
  que o catálogo não cobre).
- **Usos:**
  - a **estimativa de IRC** deixa de sugerir 21%: propõe a taxa geral do ano em
    que o período termina, só num campo vazio ou ainda com a proposta de outro
    ano, e a nota explica a taxa das PME;
  - os **códigos de IVA por omissão** leem as percentagens da tabela do ano.
- **PWA:** ecrã **«Tabelas fiscais»** (`/tabelas-fiscais`, grupo «Fiscal») —
  escolhe-se o ano e vê-se cada grupo com a fonte e a marca «Texto oficial» ou
  «Por confirmar».

**Fora da tabela, de propósito**

- **Derrama municipal** — varia por município e por ano (ofício-circulado da
  AT); entra com a Modelo 22 (F3-10).
- **Tributações autónomas antes de 2025** — as taxas e os escalões das
  viaturas eram outros; 2024 fica sem tabela em vez de mostrar valores errados.
- **A redução transitória das tributações autónomas das viaturas para PME em
  2026**, referida por uma só fonte secundária: não foi confirmada e não entra.
- Taxas regionais de IRC, regimes especiais e benefícios fiscais.
- Prazos e versões de declaração — entram com o calendário de obrigações
  (F3-27) e com cada declaração.

**Validação efetuada**

- `AccountingAI.Infrastructure.Files.Tests`: `FiscalTableCatalogTests` — a
  transição 19 / 18 / 17%; anos fora do catálogo; a taxa reduzida só na
  primeira parcela (100 000 € de uma PME em 2026 → 17 000 €); a derrama
  estadual por parcelas; o IVA por região e o estado das fontes.
- `AccountingAI.Api.Tests`: `FiscalTablesReturnTheYearWithItsSources`.
- `VatCodeServiceTests` continua a confirmar as percentagens dos Açores, agora
  vindas da tabela.
- `AccountingAI.Web`: `tsc`, `eslint`, os 89 testes; o ecrã foi aberto no
  navegador em modo de demonstração.
- A suite completa: 224 testes .NET.
- **Por verificar em direto:** o ecrã com a API e a proposta da taxa na
  estimativa de IRC.

**A reverificar**

- A leitura do texto oficial foi feita por extração automática das páginas do
  Portal das Finanças; convém uma conferência humana do art. 87.º (taxas de
  2026 a 2028) antes de qualquer uso que não seja estimativa.
- Quando sair a Lei do Orçamento para 2027, rever o catálogo inteiro.

### F3-04B — Estimativa de IRC com a taxa das PME e a derrama (resolvido no F3-10)

As duas parcelas de uma PME e a derrama estadual passaram a ser aplicadas no
Modelo 22 ([F3-10](F3-10-MODELO-22.md)); a estimativa de IRC mantém-se como
conta rápida e remete para lá.
