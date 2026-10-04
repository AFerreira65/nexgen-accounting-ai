# F3-08 — Declaração periódica de IVA

Épico do Bloco B da [Fase 3](README.md). A Fase 2 deixou o **resumo de IVA**
(movimento por natureza, taxa e região, com o campo sugerido a partir de um
manual pedagógico) e o **apuramento contabilístico** das contas 243
([F2-08](../phase-2/F2-08-VAT-SETTLEMENT.md)). Faltava a declaração em si: os
campos do modelo oficial, as somas de controlo e a conferência campo a campo.

## Fonte e vigência

- **Modelo e instruções de preenchimento da AT** (Portaria n.º 221/2017, de
  21 de julho), consultados em 2026-10-04 em
  `info.portaldasfinancas.gov.pt/pt/apoio_contribuinte/modelos_formularios/iva/`
  — é daqui que vêm os campos, o que entra em cada um e as somas dos campos
  10, 11 e 90.
- **Modelo novo a caminho.** A Portaria n.º 298/2026/1, de 16 de julho,
  aprova novos modelos (desagregação de quadros e campos, pré-preenchimento).
  Segundo o Ofício-Circulado n.º 25119, de 2026-07-22, a regra geral produz
  efeitos nas declarações de **períodos com início a partir de 1 de julho de
  2027**; desde 1 de julho de 2026 só valem o campo «Regime Grupos IVA» no
  quadro 01 e as regularizações da verba 2.42 nos anexos dos campos 40 e 41.
  A AT anuncia o novo WSDL para o início de 2027. Este épico segue o modelo
  **em vigor**; o modelo de 2027 será um incremento próprio.
- As fórmulas dos campos 91 e 92 (que parcelas somam) vêm do desenho do
  impresso, não do texto das instruções: estão **por conferir**.

## Sub-incrementos

### F3-08A — Quadro 06 e mapa de conferência (feito)

- **`VatSummaryService.PartsAsync`** — o movimento do período por conta e por
  natureza/taxa/região (as duas origens: código de IVA da linha ou
  classificação da conta), agora reutilizável.
- **`VatDeclarationService`** — preenche o quadro 06. Só lê.

| Campos | De onde vêm |
|---|---|
| 1, 5, 3 / 2, 6, 4 | Base e imposto das transmissões de bens e prestações de serviços, por taxa; e das operações em que a empresa liquida como adquirente (autoliquidação). |
| 8 | Operações isentas ou não tributadas com direito à dedução. |
| 9 | Operações isentas sem direito à dedução. |
| 12 / 13, 10 / 11 | Aquisições intracomunitárias de bens (base e imposto) e os campos de controlo. |
| 16 / 17 | Serviços de entidades de outros Estados membros. |
| 20; 21, 23, 22; 24 | Imposto dedutível: ativos não correntes; inventários por taxa; outros bens e serviços. |
| 40 / 41 | Regularizações a favor do sujeito passivo / do Estado — da totalidade da atividade, qualquer que seja a região. |
| 61 | Excesso a reportar do período anterior: o saldo da conta 2437 ou o valor indicado. |
| 65 a 68 | Totais de imposto dedutível e liquidado das operações de outra região (Anexo R), até duas regiões. |
| 90, 91, 92 | Somas de controlo. |
| 93 / 94 | Imposto a entregar ou a recuperar. |
| 95 / 96 | Sem reembolso pedido: todo o crédito segue como excesso a reportar. |

- **Campos que a aplicação não apura** (aparecem como «à mão»): 14, 15, 18
  e 19. O campo 7 passou a ser apurado com o [F3-09](F3-09-RECAPITULATIVE-STATEMENT.md). Também
  não preenche o quadro 06-A, o Anexo R nem os anexos dos campos 40 e 41.
- **Conferência:** cada campo guarda as contas que o formam e os valores.
- **Avisos:** período que não é mês nem trimestre; contas com movimento sem
  classificação de IVA; imposto dos campos 2, 6 e 4 que não corresponde à
  taxa da região sobre a base (as instruções pedem correspondência rigorosa;
  tolerância de 1 €; taxas de [F3-04](F3-04-FISCAL-TABLES.md)); necessidade
  do quadro 06-A, do Anexo R e dos anexos das regularizações; e a
  **diferença para o apuramento contabilístico** das contas 243 — zero quando
  a declaração e a contabilidade batem.
- **API:** `GET .../vat-declaration?from=&to=&priorCredit=`.
- **PWA:** ecrã **«Declaração de IVA»** (`/declaracao-iva`, grupo «Fiscal») —
  atalhos de mês e de trimestre, o quadro 06 com as colunas do impresso, cada
  campo a abrir as suas contas, os avisos, exportação CSV e impressão.

**Validação efetuada**

- `AccountingAI.IntegrationTests`: `VatDeclarationServiceTests` — vendas a
  duas taxas, venda localizada nos Açores, compras de inventários e de
  serviços, aquisição intracomunitária, regularização: campos, somas de
  controlo, contas de cada campo, avisos, diferença zero para o apuramento
  contabilístico, e o efeito do excesso a reportar; identificação do período.
  Os testes do resumo de IVA continuam a passar depois da refatoração.
- `AccountingAI.Api.Tests`: `VatDeclarationFillsTheOfficialFields` (só
  leitura).
- `AccountingAI.Web`: `tsc`, `eslint`, 95 testes, `vite build`.
- A suite completa: 239 testes .NET.

**Por verificar**

- A declaração de um período real de uma **empresa real**, comparada com a
  que foi entregue.
- As parcelas dos campos 91 e 92.

**Pressupostos a confirmar com um contabilista**

- A região de cada operação é a do código de IVA ou da classificação da
  conta; a sede é a região fiscal da empresa.
- Na autoliquidação, a base da compra entra nos campos 1, 5 e 3 pelo valor a
  débito da linha com o código de IVA.

### F3-08B — Lançamento de apuramento do IVA (feito)

- **`VatSettlementEntryService`** — propõe e gera o lançamento que fecha o IVA
  de um período:

| Passo | Lançamento |
|---|---|
| Saldar o período | Cada conta 2432 (dedutível), 2433 (liquidado) e 2434 (regularizações) é lançada ao contrário do seu saldo no período, por contrapartida da conta de apuramento (2435). |
| Crédito anterior | O saldo devedor da conta 2437 antes do período é creditado e entra a débito na 2435. |
| Resultado | Saldo credor da 2435 → IVA a pagar (2436); saldo devedor → IVA a recuperar (2437). A 2435 fica saldada. |

- Data do **fim do período**, no diário indicado, **por conferir**. As três
  contas (apuramento, a pagar, a recuperar) propõem-se pelos códigos do SNC e
  podem ser trocadas pelas subcontas da empresa.
- Trabalha sobre o que **ainda está por saldar** no período: se o apuramento
  já está na contabilidade (por exemplo, importado de um SAF-T), não propõe
  nada.
- **Recusas:** período já apurado por aqui (reconhece-se pela referência de
  documento «Apuramento IVA AAAA período»); datas que não formam um mês nem
  um trimestre; sem IVA por apurar; conta em falta ou que não é de movimento.
- **Mudança no apuramento e no resumo de IVA (Fase 2):** os lançamentos que
  movimentam a conta **2435** deixam de contar em `VatReturnService` e em
  `VatSummaryService`. Sem isto, um período depois de apurado apareceria com o
  liquidado e o dedutível a zero — tanto com o lançamento gerado aqui como
  com os apuramentos que venham de um SAF-T importado.
- **API:** `GET .../vat-settlement-entry?from=&to=&settlement=&payable=&recoverable=`
  (a proposta) e `POST` com `journal=` (o lançamento).
- **PWA:** no ecrã «Declaração de IVA», o painel «Lançamento de apuramento do
  IVA» mostra a proposta linha a linha, deixa trocar as contas e pede o
  diário.

**Validação efetuada**

- `AccountingAI.IntegrationTests`: `VatSettlementEntryServiceTests` — três
  trimestres seguidos: IVA a pagar; IVA a recuperar; crédito anterior a
  entrar no apuramento seguinte; conta de apuramento saldada; segunda
  tentativa recusada; o apuramento contabilístico e a declaração continuam a
  mostrar os valores do período depois do lançamento; sem movimento e conta
  em falta. Os testes do apuramento e do resumo de IVA da Fase 2 continuam a
  passar.
- `AccountingAI.Api.Tests`: a proposta (só leitura).
- A suite completa: 253 testes .NET, 101 de interface.
- **Por verificar em direto:** o painel, com a base real — é uma operação de
  escrita.

**Pressupostos a confirmar com um contabilista**

- O apuramento num só lançamento, com a conta 2435 a débito e a crédito pelos
  totais, em vez de um lançamento por conta.
- Reconhecer um lançamento de apuramento por movimentar a conta 2435 (e não
  por um tipo de documento).
- O reembolso pedido (2438) não é tratado: todo o crédito fica na 2437.

### A seguir

- **F3-08C** — ficheiro XML da declaração para carregar no Portal das
  Finanças. **Bloqueado**: não encontrei o XML-Schema oficial publicado
  (a página «Suporte informático — formato de ficheiros» devolve erro); sem
  ele não se gera nada.
- **F3-08D** — quadro 06-A, Anexo R e anexos dos campos 40 e 41.
- **Modelo de 2027** (Portaria n.º 298/2026/1) quando a AT publicar as
  instruções e o WSDL.
