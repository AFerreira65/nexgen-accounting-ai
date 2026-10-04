# F3-18 — Lançamentos sugeridos

Épico do Bloco D da [Fase 3](README.md). O e-Fatura já trazia para a
plataforma as faturas de compra que a AT conhece, e o cruzamento dizia quais
faltavam na contabilidade. Lançá-las continuava a ser trabalho à mão, fatura
a fatura.

## Sub-incrementos

### F3-18A — Lançamentos sugeridos a partir das compras do e-Fatura (feito)

- **`Domain/Accounting/SupplierEntryRule`**: a regra de lançamento de um
  fornecedor. Liga o NIF do fornecedor a um
  [lançamento-tipo](F3-03-VAT-CODES.md) (F3-03D), que já guarda o diário, o
  tipo de documento e as contas de gasto, de IVA e da contrapartida. Há uma
  regra por NIF e por empresa.
- **`EFaturaPurchase.JournalEntryReference`**: a referência do lançamento
  criado a partir da fatura. Guarda-se a referência e não o Id, porque
  corrigir um lançamento recria-o com a mesma referência.
- **Migração aditiva** `20261004221135_SupplierEntryRules`: uma tabela nova e
  uma coluna anulável. A regra é auditável e ambas estão no dicionário de
  dados.
- **`EntrySource.EFatura`**: a origem dos lançamentos criados daqui, que
  aparece como «Sugerido do e-Fatura».
- **`SuggestedEntryService`** aplica estas regras:

| Parte | Regra |
|---|---|
| Faturas por lançar | As do período que **não** têm um lançamento ligado por anular nem um lançamento de compra encontrado pelo cruzamento. O critério é o mesmo do ecrã «Cruzamento»: número do documento normalizado e ±31 dias (`CrossCheckBuilder.BookedInvoiceIds`). As anuladas no Portal (situação «Anulada») não são sugeridas. |
| Valores | A base e o IVA vêm da fatura tal como estão, sem recálculo (`EntryTemplate.ComputeForDocument`). Se o lançamento-tipo não tiver linha de IVA, o imposto não é dedutível e soma-se à base. Se tiver várias linhas de IVA, estas calculam-se pela base com as percentagens dos códigos. |
| Nota de crédito | Quando o tipo é `NC` ou «Nota de crédito», ou o total vem negativo, os lados do lançamento-tipo invertem-se. É um critério da aplicação, porque o CSV do Portal não tem um campo próprio. |
| Contrapartida | Leva o fornecedor como terceiro quando há uma ficha ativa com aquele NIF. Se a ficha tiver conta própria de fornecedor (221…), essa conta substitui a do lançamento-tipo. Sem ficha, a sugestão avisa e a contrapartida fica sem terceiro. |
| Descrição | A da regra; se faltar, a do lançamento-tipo; se faltar também, «Fatura \<número\> — \<fornecedor\>». |
| Finalizar | Cria o lançamento **por conferir** pelo `JournalPostingService`, com as regras habituais (período bloqueado, exercício apurado, contas de movimento). A data do lançamento e a do documento são a da fatura, e o documento é o número da fatura. A referência fica guardada na fatura. Cada fatura é independente: uma que falhe fica com o erro e as outras seguem. |
| Anular | Anular o lançamento devolve a fatura às sugestões. |

- **API:**
  - `GET .../suggested-entries?from=&to=` devolve as faturas por lançar com
    a sugestão de cada uma e as contagens do período.
  - `POST .../suggested-entries/finalize` (`{ purchaseIds }`) devolve a
    referência ou o erro de cada fatura.
  - `GET .../supplier-entry-rules`, `PUT .../supplier-entry-rules/{nif}`
    (`{ entryTemplateCode, description, isActive }`) e
    `DELETE .../supplier-entry-rules/{nif}`.
- **PWA:** ecrã **«Lançamentos sugeridos»** (`/lancamentos-sugeridos`, grupo
  «Trabalho»):
  - o período e as contagens;
  - os fornecedores sem regra, cada um com a escolha do lançamento-tipo;
  - as faturas por lançar, com as linhas propostas e as prontas já
    selecionadas;
  - o botão «Finalizar», com as ligações aos lançamentos criados;
  - a lista das regras e o CSV.

  As «Tarefas» passam a ligar a este ecrã quando há faturas sem
  correspondência.

**Validação efetuada**

- `NexGenAccountingAI.Domain.Tests` (`EntryTemplateTests`): o documento
  mantém a sua base e o seu IVA, mesmo com o arredondamento do fornecedor; a
  nota de crédito troca os lados; sem linha de IVA, o imposto é gasto; o IVA a
  zero não gera linha.
- `NexGenAccountingAI.IntegrationTests` (`SuggestedEntryServiceTests`, em
  PostgreSQL):
  - **Regra:** guardar com o NIF por normalizar; lançamento-tipo
    inexistente.
  - **Lista:** fatura já lançada à mão e fatura anulada fora da lista;
    fornecedor sem regra; contrapartida na conta própria do fornecedor, com
    terceiro; nota de crédito invertida.
  - **Finalizar:** lançamento por conferir, com origem e-Fatura, documento,
    data do documento e terceiro; erro por fatura sem regra e por fatura já
    lançada; a fatura sai das sugestões; anular o lançamento devolve-a.
  - **Fecho:** apagar regras; sem acesso.
- `DatabaseMigrationTests`: a migração nova aplica-se duas vezes.
- `NexGenAccountingAI.Api.Tests`: lista, período invertido, finalizar sem
  faturas, regras, regra com lançamento-tipo inexistente e apagar uma regra
  que não existe.
- PWA: `App.test.tsx` cobre a rota, o estado sem base de dados e a entrada no
  menu; typecheck e lint sem erros.
- **Não visto com a base real:** a base de trabalho só abre com a sessão do
  utilizador.

### F3-18B — Lançamentos sugeridos a partir das vendas do SAF-T de faturação (feito)

- **`SaftSalesDocumentsReader`** (`Infrastructure.Files`): lê, documento a
  documento e em streaming, o bloco `SalesInvoices`.
  - Cada documento traz o cliente, resolvido pelos `Customer` dos
    `MasterFiles`, e a base e o imposto **por taxa de IVA**.
  - A base de cada taxa é a soma das linhas (`CreditAmount` menos
  `DebitAmount`).
  - O imposto calcula-se pela percentagem. O acerto ao cêntimo até ao
    `NetTotal` e ao `TaxPayable` do documento fica na taxa com mais base.
  - Num SAF-T real com 1190 documentos, a soma das partes bateu com os
    totais de todos eles.
- **`Domain/Billing/SalesDocument`**: o documento importado. A repartição por
  taxa fica numa coluna `jsonb`.
- **`Domain/Accounting/SalesEntryRule`**: a regra de vendas por percentagem
  de IVA, que liga uma taxa a um lançamento-tipo.
- **Migração aditiva** `20261004222709_SalesDocuments`: duas tabelas novas.
- **`EntrySource.SaftBilling`**: a origem «Sugerido do SAF-T de faturação».
- **`SalesSuggestedEntryService`** aplica estas regras:

| Parte | Regra |
|---|---|
| Importar | Idempotente pelo número do documento. Recusa um ficheiro cujo NIF não seja o da empresa ativa. Tem pré-visualização (`dryRun`). |
| Documentos por lançar | Os do período não anulados e sem um lançamento ligado por anular nem um lançamento de vendas (linhas em 71, 72 ou 2433) com o mesmo número de documento, ±31 dias. O critério é o do cruzamento. |
| Valores | Para cada taxa do documento, aplica-se o lançamento-tipo da regra dessa taxa à base e ao IVA dessa parte (`ComputeForDocument`). As linhas com a mesma conta, o mesmo código de IVA e o mesmo papel juntam-se, e o cliente fica numa só linha. Um documento com uma taxa sem regra não é sugerido. |
| Nota de crédito | O tipo `NC` inverte os lados. |
| Cliente | Se o cliente tiver ficha de terceiro ativa (país e NIF), vai na linha do cliente, com a conta própria quando a ficha a tiver. O consumidor final (NIF 999999990) fica na conta do lançamento-tipo, sem terceiro. |
| Finalizar | Igual às compras: lançamento por conferir, ligado ao documento, com erro por documento. |

- **API:**
  - `POST .../sales-documents/import?dryRun=&fileName=`, com o XML no corpo;
  - `GET .../sales-suggested-entries?from=&to=`;
  - `POST .../sales-suggested-entries/finalize` (`{ documentIds }`);
  - `GET .../sales-entry-rules`, `PUT` e `DELETE .../sales-entry-rules/{percentagem}`.
- **PWA:** o ecrã «Lançamentos sugeridos» tem agora dois separadores,
  «Compras (e-Fatura)» e «Vendas (SAF-T de faturação)», com o período
  partilhado. O separador das vendas tem:
  - a importação do ficheiro, com pré-visualização;
  - as regras por taxa, com as taxas ainda sem regra;
  - os documentos por lançar e o botão «Finalizar».

  O resultado de finalizar indica `documentId` nos dois separadores.
- **Limitação:** as vendas lançadas por resumo mensal não se encontram pelo
  número do documento. Nesse caso, os documentos aparecem por lançar e não
  devem ser finalizados; o ecrã avisa.

**Validação efetuada**

- `NexGenAccountingAI.Infrastructure.Files.Tests` (`SaftSalesDocumentsReaderTests`):
  - duas taxas no mesmo documento, com o acerto ao cêntimo;
  - nota de crédito com valores positivos;
  - consumidor final e documento anulado;
  - ficheiro sem vendas e XML ilegível.
- `NexGenAccountingAI.IntegrationTests` (`SalesSuggestedEntryServiceTests`, em
  PostgreSQL, com um leitor falso):
  - **Importar:** pré-visualização sem escrita, reimportação sem duplicar e
    ficheiro de outra empresa recusado.
  - **Lista:** documento já lançado à mão fora da lista; taxa sem regra.
  - **Sugestão:** documento a duas taxas com cinco linhas, cliente na conta
    própria e com terceiro; nota de crédito de consumidor final sem
    terceiro.
  - **Finalizar:** lançamento por conferir com a origem certa; o documento
    sai das sugestões.
  - **Regras e acesso:** regras, lançamento-tipo inexistente e sem acesso.
- `NexGenAccountingAI.Api.Tests`: lista, regras, regra com lançamento-tipo
  inexistente e corpo que não é XML.

### F3-18C — «Não lançar» e documentos em falta (feito)

- **`EFaturaPurchase.DismissedReason` / `DismissedAtUtc`**: uma fatura do
  e-Fatura pode marcar-se «não lançar» com um motivo, por exemplo uma despesa
  particular, um duplicado ou um documento lançado noutro. Migração aditiva
  `20261004223551_EFaturaDismissal`, com duas colunas anuláveis.
- **Efeito:** uma fatura dispensada sai das sugestões e também do
  **cruzamento**, que deixa de a contar nos totais e na lista de faturas sem
  lançamento. Assim também sai das «Tarefas».
- **Regras:**
  - uma fatura com lançamento por anular não se dispensa;
  - o motivo é obrigatório;
  - repor devolve a fatura às sugestões e ao cruzamento.
- **API:** `POST .../suggested-entries/dismiss` (`{ purchaseIds, reason }`)
  e `POST .../suggested-entries/restore` (`{ purchaseIds }`). A lista
  `GET .../suggested-entries` traz as dispensadas do período (`dismissed`).
- **PWA**, no separador «Compras»:
  - selecionar qualquer fatura, mesmo sem sugestão pronta, e marcá-la «não
    lançar» com um motivo (há motivos propostos);
  - o painel «Não lançar», com o botão para repor;
  - o CSV **«Documentos em falta»**: as faturas por lançar agrupadas por
    fornecedor (NIF, número de faturas, total e a lista de documentos),
    ordenadas pelo total, para pedir ao cliente.

**Validação efetuada**

- `SuggestedEntryServiceTests` cobre:
  - dispensar com o motivo normalizado;
  - recusar a dispensa de uma fatura com lançamento;
  - motivo vazio;
  - a fatura sai das sugestões e do cruzamento;
  - repor.
- `NexGenAccountingAI.Api.Tests`: dispensar sem faturas.

O épico F3-18 fica concluído.

### Ideias para depois

- Regras por conta ou por setor do e-Fatura, além do NIF.
- Os lançamentos de compra sem fatura no e-Fatura, que o cruzamento já lista,
  com o seguimento de cada um.
