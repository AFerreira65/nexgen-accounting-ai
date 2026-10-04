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

### Por fazer

- **F3-18B**: as vendas a partir do SAF-T de faturação. O leitor atual só
  soma totais, por isso é preciso ler documento a documento, com o cliente.
- **F3-18C**: o mapa de documentos em falta, ou seja, os lançamentos de
  compra sem fatura no e-Fatura (o inverso desta lista), com o seguimento de
  cada um.
- Regras por conta ou por setor do e-Fatura, além do NIF.
