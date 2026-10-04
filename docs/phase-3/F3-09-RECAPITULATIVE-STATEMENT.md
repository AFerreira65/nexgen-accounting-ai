# F3-09 — Declaração recapitulativa de IVA

Épico do Bloco B da [Fase 3](README.md). A declaração recapitulativa lista,
por adquirente, as transmissões intracomunitárias de bens, as operações
triangulares e as prestações de serviços a sujeitos passivos de outros
Estados-Membros. Faltavam na aplicação as próprias naturezas de IVA destas
operações — e, com elas, o campo 7 da declaração periódica
([F3-08](F3-08-VAT-DECLARATION.md)).

## Fonte

Modelo da declaração recapitulativa e instruções de preenchimento da AT,
consultados em 2026-10-04
(`info.portaldasfinancas.gov.pt/pt/apoio_contribuinte/modelos_formularios/iva/Documents/declaracao_recapitulativa.pdf`):
os tipos de operação 1, 4 e 5, a agregação por adquirente, o arredondamento
ao euro, o prefixo `XI` da Irlanda do Norte só para bens e a regra de que as
operações de tipo 4 vão ao campo 8 da declaração periódica, não ao 7.

## Sub-incrementos

### F3-09A — Naturezas intracomunitárias, quadros 04 e 05 (feito)

- **Três naturezas de IVA novas** (`VatOperationNature`, sem migração — o
  valor é guardado como inteiro): transmissões intracomunitárias de bens
  (13), prestações de serviços intracomunitárias (14) e operações
  triangulares (15). Disponíveis nos códigos de IVA e na classificação por
  conta; os códigos por omissão passam a incluir `V-TIB`, `S-PSI` e `V-TRI`
  (numa empresa existente, «criar os códigos em falta» acrescenta-os).
- **Declaração periódica:** o campo 7 deixa de ser «à mão» (naturezas 13 e
  14); as operações triangulares entram no campo 8; um aviso lembra a
  declaração recapitulativa quando o campo 7 tem valor.
- **`RecapitulativeStatementService`** — só lê:

| Parte | Regra |
|---|---|
| Operações | As linhas do período (não anuladas, fora das contas 243) cuja natureza — do código de IVA da linha ou, sem código, da classificação da conta — é uma das três. Valor = crédito − débito, pelo que as notas de crédito abatem. |
| Adquirente | O terceiro da linha; se a linha não o tem, o único terceiro do lançamento (normalmente o da linha do cliente). |
| Quadro 04 | Uma linha por prefixo + NIF + tipo (1 bens, 4 triangulares, 5 serviços), com o valor arredondado ao euro. A Grécia leva o prefixo `EL`; o prefixo do país é retirado do início do número. |
| Quadro 05 | Campo 10 (tipo 1), 17 (tipo 4), 18 (tipo 5) e 19 = tipos 1 + 5, o valor que deve coincidir com o campo 7 da declaração periódica. |
| De fora, com o motivo | Lançamento sem terceiro ou com mais de um; adquirente que não é de outro Estado-Membro (Portugal, país terceiro, ou serviços para a Irlanda do Norte); adquirente sem número de identificação. |

- **API:** `GET .../recapitulative-statement?from=&to=`.
- **PWA:** ecrã **«Declaração recapitulativa»** (`/declaracao-recapitulativa`,
  grupo «Fiscal») — atalhos de mês e trimestre, os campos do quadro 05, a
  tabela do quadro 04, os movimentos que ficaram de fora, CSV e impressão.

**Validação efetuada**

- `NexGenAccountingAI.IntegrationTests`: `RecapitulativeStatementServiceTests` —
  agregação de duas vendas e uma nota de crédito ao mesmo adquirente, serviço
  a um cliente grego (`EL`), operação triangular, lançamento sem terceiro e
  adquirente de país terceiro; campos 10, 17, 18 e 19; e os campos 7 e 8 da
  declaração periódica para os mesmos dados.
- `NexGenAccountingAI.Api.Tests`: o endpoint (só leitura).
- `NexGenAccountingAI.Web`: `tsc`, `eslint`, 96 testes, `vite build`.
- A suite completa: 240 testes .NET.

**Por verificar**

- Com uma **empresa real**. Os lançamentos importados de um SAF-T não trazem
  código de IVA: só entram se as contas de vendas intracomunitárias estiverem
  classificadas com uma das naturezas novas.
- A fórmula do campo 19 (tipos 1 + 5 + campo 11) vem do texto do impresso e
  das instruções da declaração periódica; **por conferir**.

**Fora do âmbito**

- Campo 11 (meios de transporte novos a particulares), quadro 06
  (transferências de bens à consignação), declarações de substituição e a
  confirmação dos números no VIES.
- A entrega: o serviço da AT (`docs/at-wsdl/iva-recapitulativa/`) exige o
  certificado de produtor — Bloco C.

### A seguir

- **F3-09B** — entrega pelo webservice da AT, quando houver certificado de
  testes (o XSD do pedido já está em `docs/at-wsdl/iva-recapitulativa/`).
