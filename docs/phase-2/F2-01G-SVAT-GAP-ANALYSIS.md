# F2-01G — Inventário SVAT: o que falta para o Selo de Validação AT

Último sub-incremento do [F2-01](F2-01-SAFT-XSD-VALIDATION.md). Só
documento — não há código de produção. Serve para saber, com honestidade,
a distância a que a plataforma está de poder pedir o SVAT, e o que os
sub-incrementos anteriores já cobrem.

## O que é o SVAT

O **Selo de Validação AT (SVAT)** é um processo **facultativo** para
*programas de contabilidade que produzem o SAF-T (PT) de contabilidade*
(ver [`../CERTIFICACAO_AT.md`](../CERTIFICACAO_AT.md) e a
[página oficial](https://info.portaldasfinancas.gov.pt/pt/apoio_ao_contribuinte/Negocios/Faturacao/SAF_T_PT/SVAT/Paginas/default.aspx)).
O produtor submete um SAF-T de contabilidade gerado pelo programa e a AT
verifica a sua conformidade estrutural (XSD) e um conjunto de regras
analíticas e de coerência.

## Pré-condição não satisfeita

**A plataforma não produz um SAF-T de contabilidade.** Hoje lê, valida,
importa e reconcilia SAF-T de terceiros; não há um módulo que **exporte**
o SAF-T de contabilidade da empresa a partir do núcleo contabilístico.
Sem esse ficheiro de saída não há o que submeter ao SVAT.

Portanto, **não faz sentido iniciar um pedido de SVAT neste estado.** O
que F2-01 construiu — validação de esquema + regras analíticas — é
exatamente o tipo de verificação que o SVAT aplica, e fica como base do
auto-teste do futuro ficheiro exportado.

## Mapa dos controlos

Estado: **✅** coberto pelas camadas de F2-01 · **🟡** aferível sobre um
ficheiro lido, ainda por implementar · **⛔** depende de existir a
exportação do SAF-T de contabilidade.

| Categoria de controlo | Estado | Onde / o que falta |
|---|---|---|
| Conformidade com o XSD oficial (ordem, cardinalidades, tipos, enumerações) | ✅ / ⛔ | `SaftSchemaValidator` (F2-01A); falta **colocar o XSD oficial** (drop-in, decisão de redistribuição em aberto). Sobre a saída: depende da exportação |
| Versão (`AuditFileVersion`) suportada e coerente com o namespace | ✅ | F2-01A |
| `TaxRegistrationNumber` com dígito de controlo válido | ✅ | `NIF-CHECKDIGIT` (F2-01D) |
| NIF de clientes e fornecedores (`Customer`/`Supplier`) com dígito de controlo | 🟡 | o modelo de validação ainda não lê `MasterFiles/Customer`/`Supplier` |
| `TaxAccountingBasis` num valor previsto | ✅ | `BASIS-KNOWN` (F2-01D) |
| `Period`/`StartDate`/`EndDate`/`FiscalYear` coerentes | ✅ | `PERIOD-RANGE` (F2-01D) |
| `GeneralLedgerEntries/NumberOfEntries` = nº de transações | ✅ | `ENTRY-COUNT` (F2-01D) |
| `TotalDebit`/`TotalCredit` = soma das linhas e iguais entre si (ao cêntimo) | ✅ | `FILE-TOTALS` (F2-01D) |
| Cada transação equilibrada | ✅ | `TRANSACTION-BALANCED` (F2-01D) |
| Todo o `AccountID` referido resolve no plano | ✅ | `ACCOUNT-REF-RESOLVES` (F2-01D) |
| Datas dos lançamentos dentro do período | ✅ | `ENTRY-DATES-IN-PERIOD` (F2-01D) |
| Base de contabilidade com diários | ✅ | `ACCOUNTING-HAS-JOURNALS` (F2-01D) |
| Saldos de abertura/fecho por conta coerentes com o movimento do período | 🟡 | `OPENING-CLOSING-CONSISTENT` — o modelo ainda não lê os saldos por conta |
| Cobertura de `TaxonomyCode`/`TaxonomyReference` nas contas de movimento | 🟡 | a plataforma já controla isto internamente (mapa de revisão de taxonomias); falta uma regra que o afira sobre o ficheiro |
| `ProductID` / `ProductCompanyTaxID` / `SoftwareCertificateNumber` presentes e corretos | ✅ (presença) / ⛔ (valor) | `PRODUCT-COMPANY-TIN` verifica a presença; o **número de certificado real** só existe depois da certificação Modelo 24 |
| Numeração cronológica e sem saltos dos lançamentos; imutabilidade do que está finalizado; trilho de auditoria | ⛔ | requisitos do *programa*, não do ficheiro; ligam-se ao caminho da Modelo 24 em `CERTIFICACAO_AT.md` |
| Assinatura/encadeamento e restantes regras do Despacho 8632/2014 | ⛔ | fora do âmbito da contabilidade; só relevante para faturação certificada |

## Lacunas a fechar antes de um pedido de SVAT

1. **Módulo de exportação do SAF-T de contabilidade** a partir do núcleo
   (cabeçalho, `GeneralLedgerAccounts` com saldos, `GeneralLedgerEntries`
   com as linhas, `Customer`/`Supplier` se aplicável), conforme o XSD
   1.04_01. É o item que desbloqueia tudo o resto.
2. **Colocar o XSD oficial** da AT (resolver a decisão de redistribuição)
   para a validação de esquema deixar de ser só «boa formação».
3. **Alargar o `SaftValidationModel`** para trazer os saldos de
   abertura/fecho por conta e os NIF de clientes/fornecedores, e
   implementar as regras 🟡 (`OPENING-CLOSING-CONSISTENT`, NIF de
   terceiros, cobertura de taxonomia sobre o ficheiro).
4. **Correr o validador de F2-01 sobre o ficheiro exportado** como
   auto-teste antes de qualquer submissão, e guardar o relatório
   (`SaftValidationRun` já suporta isto).
5. **Dossier**: exemplar instalável, base de teste, manual, dicionário de
   dados, cenários e o SAF-T gerado nos testes — como descrito em
   `CERTIFICACAO_AT.md`.

## Recomendação

Não pedir o SVAT enquanto não existir a exportação do SAF-T de
contabilidade. Tratar F2-01 como a fundação de conformidade: as regras
analíticas e o validador de esquema são reutilizáveis, sem alteração,
sobre o ficheiro que a plataforma vier a produzir. Quando esse módulo
existir, reabrir este documento e converter as linhas 🟡/⛔ em trabalho.

## Referências

- [`../CERTIFICACAO_AT.md`](../CERTIFICACAO_AT.md) — enquadramento de
  certificação (Modelo 24, e-Fatura, SVAT).
- [Selo de Validação AT (SVAT)](https://info.portaldasfinancas.gov.pt/pt/apoio_ao_contribuinte/Negocios/Faturacao/SAF_T_PT/SVAT/Paginas/default.aspx)
- Portaria n.º 302/2016 — estrutura do SAF-T (PT) de contabilidade (1.04_01).
- [F2-01](F2-01-SAFT-XSD-VALIDATION.md) e sub-incrementos A–F.
