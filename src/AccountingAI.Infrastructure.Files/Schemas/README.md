# Esquemas oficiais do SAF-T (PT)

O validador de esquema (`SaftSchemaValidator`) valida os ficheiros SAF-T
importados contra o XSD **oficial da Autoridade Tributária**. Esse XSD **não
está no repositório** — a redistribuição está por confirmar (ver
`docs/phase-2/F2-01-SAFT-XSD-VALIDATION.md`, «Decisões em aberto»).

## Como ativar a validação de esquema

1. Obter os esquemas oficiais no Portal das Finanças:
   - **1.04_01** (Portaria n.º 302/2016) — SAF-T de contabilidade atual;
   - **1.03_01** (Portaria n.º 1192/2009 / n.º 160/2013) — versão anterior.
2. Colocar os ficheiros nesta pasta com estes nomes exatos:
   - `SAFTPT_1.04_01.xsd`
   - `SAFTPT_1.03_01.xsd`
3. Reconstruir a solução. Os ficheiros `SAFTPT_*.xsd` são embebidos no
   assembly (`AccountingAI.Infrastructure.Files.csproj`) e o catálogo
   (`SaftSchemaCatalog.Official`) passa a indexá-los pelo `targetNamespace`
   de cada um.

## Sem os ficheiros

Enquanto a pasta não tiver nenhum `SAFTPT_*.xsd`, o validador **não falha**:
devolve um relatório com `schemaAvailable = false`, valida apenas a boa
formação do XML e assinala, com um aviso, que o esquema oficial não está
carregado. O resto da plataforma (leitura, pré-visualização, importação e
reconciliação) funciona na mesma.

## Notas

- Um `.xsd` com um `targetNamespace` diferente de
  `urn:OECD:StandardAuditFile-Tax:PT_1.04_01` /
  `…PT_1.03_01` é aceite na mesma — o catálogo indexa pelo namespace que o
  próprio ficheiro declara.
- Não colocar aqui esquemas de terceiros nem versões modificadas.
