# F3-21 — Classificação das compras (histórico e IA)

Épico do Bloco D da [Fase 3](README.md). Os lançamentos sugeridos
([F3-18](F3-18-SUGGESTED-ENTRIES.md)) precisam de uma regra por fornecedor, e
criá-las uma a uma era o trabalho que restava. A classificação propõe-nas, com
a explicação de onde vêm, e o utilizador **confirma sempre**.

## Sub-incrementos

### F3-21A — Proposta pelo histórico do fornecedor (feito)

- **`PurchaseClassificationService.ProposeAsync`**: lê até 200 lançamentos do
  fornecedor, não anulados e sem abertura nem apuramento. Entram os que têm o
  fornecedor como terceiro numa linha e os que foram criados a partir de uma
  fatura dele no e-Fatura. Em cada lançamento:
  - **conta de gasto:** a linha de maior valor nas classes 3, 4 ou 6;
  - **IVA:** a linha em 2432 e o código de IVA;
  - **fornecedor:** a linha em 22;
  - **diário:** o do lançamento.

  A proposta é a escolha **mais frequente** de cada uma. A explicação conta as
  compras analisadas e quantas usaram a conta, por exemplo «Em 3 compras
  lançadas deste fornecedor, a conta 6221 (…) foi usada em 2; o IVA levou o
  código C-OBS-NOR em 2». A confiança é a parte das compras com essa conta;
  abaixo de 60 %, a explicação pede para confirmar.
- **Aceitar:** cria (ou substitui) o lançamento-tipo **`H-<NIF>`** com a base,
  o IVA e o fornecedor propostos, e a regra do fornecedor. O utilizador pode
  corrigir a conta antes de aceitar.

### F3-21B — Proposta da IA para fornecedores sem histórico (feito)

- **`IPurchaseClassifier`** (`Application`) e **`ClaudePurchaseClassifier`**
  (`Infrastructure.Ai`, a única fronteira com o SDK, ADR-0006). A chave é a
  mesma dos relatórios de gestão; sem chave, a funcionalidade fica desligada e
  o ecrã não mostra o botão.
- **Dossiê determinístico.** Só sai da plataforma:
  - o NIF, o nome e o setor do fornecedor declarado no e-Fatura;
  - até 10 faturas dele (data, tipo, base e IVA);
  - as contas de movimento das classes 3, 4 e 6 da empresa (até 400);
  - os códigos de IVA ativos.

  Nenhum outro dado da contabilidade vai ao modelo.
- **Validação:** o modelo escolhe de entre as listas e responde em JSON. A
  conta tem de ser uma das contas enviadas; um código de IVA que não exista é
  ignorado. A conta de IVA, a do fornecedor e o diário vêm do uso da empresa:
  a conta de IVA mais usada com esse código, a conta própria do fornecedor ou
  a conta de fornecedores mais usada nas compras, e o diário das compras.
- **A explicação** do modelo é mostrada com a indicação «proposta da IA —
  confirme antes de aceitar». Aceitar segue o mesmo caminho do F3-21A.
- **API:**
  - `POST .../suggested-entries/classification/history` (`{ supplierTaxIds }`);
  - `GET .../suggested-entries/classification/ai-status`;
  - `POST .../suggested-entries/classification/ai` (`{ supplierTaxId }`);
  - `POST .../suggested-entries/classification/accept`, com a conta, o
    código de IVA, a conta de IVA, a conta do fornecedor e o diário
    confirmados.
- **PWA:** no separador «Compras» dos lançamentos sugeridos, os fornecedores
  sem regra mostram a proposta pelo histórico, com a conta editável, a
  explicação e o botão «Aceitar proposta». Sem histórico e com a IA
  configurada, mostram o botão «Pedir à IA».

**Validação efetuada**

- `NexGenAccountingAI.IntegrationTests` (`PurchaseClassificationServiceTests`,
  em PostgreSQL, com um classificador falso):
  - **Histórico:** proposta pelo histórico (conta, confiança 0,67, código e
    conta de IVA, fornecedor, diário e explicação); aceitar cria `H-<NIF>` e
    a regra, e a fatura passa a ter a sugestão pronta; um fornecedor sem
    histórico não tem proposta.
  - **IA:** proposta com o dossiê só com as classes 3, 4 e 6 e o setor; as
    contas de IVA e do fornecedor vêm do uso; conta fora do plano recusada;
    IA não configurada recusada.
  - **Aceitar:** com as contas corrigidas; IVA sem conta de IVA recusado.
- `NexGenAccountingAI.Api.Tests`: histórico, estado da IA e aceitação
  inválida.
- **Não testado com o modelo real.** Exige a chave da API, que não está
  configurada nesta máquina.

### Por fazer

- Classificar também as vendas por cliente, quando a regra por taxa não chega.
- Aprender com as correções: quando o lançamento sugerido é corrigido antes de
  conferido, propor a atualização da regra.
