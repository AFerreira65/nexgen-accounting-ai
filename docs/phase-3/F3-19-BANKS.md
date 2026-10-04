# F3-19 — Bancos

Épico do Bloco D da [Fase 3](README.md). A conta de depósitos só se
conferia à vista: não havia na plataforma o extrato do banco com que a
comparar.

## Sub-incrementos

### F3-19A — Extratos, conciliação e mapa de reconciliação (feito)

- **`BankStatementReader`** (`Infrastructure.Files`):
  - **ISO 20022 camt.053**, em qualquer versão, porque os elementos se leem
    pelo nome local. Lê os saldos `OPBD`/`PRCD` e `CLBD`, o IBAN e os
    movimentos `BOOK`; os pendentes ficam de fora. O descritivo vem dos
    `Ustrd` ou do `AddtlNtryInf`, e a referência do `AcctSvcrRef` ou do
    `EndToEndId`. O DTD está proibido e não há resolução externa.
  - **CSV dos bancos**:
    - o cabeçalho procura-se nas primeiras 30 linhas, porque os bancos põem
      antes os dados da conta;
    - as colunas reconhecem-se pelo nome sem acentos: data do movimento,
      data-valor, descrição, débito e crédito ou montante com sinal, saldo e
      referência;
    - aceita UTF-8 e Windows-1252, `;`, `,` ou tabulação, e montantes como
      «1.234,56», «1,234.56», «-12,30» e «12,30-»;
    - as linhas sem data (totais, rodapés) são ignoradas;
    - os saldos inicial e final deduzem-se do saldo das linhas, por ordem
      ascendente ou descendente.
- **`Domain/Banking`**:
  - **`BankStatementImport`**: cada ficheiro, com o formato, o IBAN e os
    saldos declarados.
  - **`BankStatementLine`**: cada movimento, numa conta de depósitos de
    movimento (11, 12 ou 13). O montante é positivo quando entra dinheiro,
    com o mesmo sinal que débito menos crédito.
  - A chave de idempotência é um SHA-256 da data, do montante, da descrição
    normalizada, da referência e da **ordem entre movimentos iguais no mesmo
    ficheiro**: dois cafés iguais no mesmo dia são dois movimentos, e um
    extrato que se sobreponha a outro não duplica.
  - Migração aditiva `20261004224533_BankStatements`, com duas tabelas novas.
- **`BankStatementService`** aplica estas regras:

| Parte | Regra |
|---|---|
| Importar | Com pré-visualização. As linhas ilegíveis vêm reportadas e os movimentos de montante zero são ignorados. A conta tem de ser de depósitos e de movimento. |
| Lançamento visto do banco | O efeito líquido do lançamento na conta (débito menos crédito); os anulados não contam. |
| Conciliação automática | Cada movimento por conciliar fica com um lançamento da conta ainda livre e com o **mesmo valor**, a até **7 dias**: o mais próximo na data primeiro, um para um. É um critério da aplicação. |
| Conciliação manual | O lançamento tem de movimentar a conta pelo mesmo valor e não estar conciliado com outro movimento. Desfaz-se a qualquer momento. |
| Lançamento anulado | Deixa de contar como conciliado: o movimento volta a «por conciliar». |
| Mapa de reconciliação | Mostra numa data: o saldo contabilístico; o saldo do extrato (o da última linha com saldo ou o declarado no último ficheiro); os movimentos **só no banco**; os lançamentos **só na contabilidade**, desde o primeiro extrato; e a **diferença por explicar**, que é o saldo do extrato menos (contabilístico + só no banco − só na contabilidade). |

- **API:**
  - `GET .../bank-accounts`;
  - `POST .../bank-statements/import?accountCode=&dryRun=&fileName=`;
  - `GET .../bank-statements/lines?accountCode=&from=&to=`;
  - `POST .../bank-statements/auto-reconcile?accountCode=&from=&to=`;
  - `PUT` e `DELETE .../bank-statements/lines/{id}/reconciliation`;
  - `GET .../bank-statements/reconciliation?accountCode=&date=`.
- **PWA:** ecrã **«Bancos»** (`/bancos`, grupo «Trabalho»):
  - a conta e o período;
  - a importação com pré-visualização, que avisa quando o IBAN do ficheiro é
    diferente do dos extratos anteriores;
  - os saldos e a diferença por explicar;
  - os movimentos, com a conciliação automática, a conciliação manual (que
    propõe os lançamentos com o mesmo valor) e o botão para a desfazer;
  - os lançamentos só na contabilidade;
  - o CSV.

**Validação efetuada**

- `NexGenAccountingAI.Infrastructure.Files.Tests` (`BankStatementReaderTests`):
  - CSV com preâmbulo em Windows-1252, com débito e crédito e linha de
    totais;
  - CSV com montante com sinal, por ordem descendente;
  - linhas ilegíveis e CSV irreconhecível;
  - camt.053 com saldos, `Ustrd` repetido, `DtTm` e um movimento pendente;
  - um XML que não é camt.
- `NexGenAccountingAI.IntegrationTests` (`BankStatementServiceTests`, em PostgreSQL):
  - **Importar:** pré-visualização; dois movimentos iguais; reimportação
    sem duplicar; conta que não é de depósitos.
  - **Conciliar:** a automática escolhe o lançamento mais próximo; a
    manual recusa um valor diferente; desfazer.
  - **Mapa:** fecha com diferença zero.
  - **Anulação, contas e acesso:** a anulação liberta a conciliação; lista
    das contas com o IBAN; sem acesso.
- `NexGenAccountingAI.Api.Tests`: contas, ficheiro irreconhecível e conta
  errada.
- PWA: rota e estado sem base de dados.
- **Não visto com extratos reais.** Os CSV variam de banco para banco; um
  formato que não seja reconhecido dá um erro claro.

### Por fazer

- **F3-19B**: lançar a partir do extrato, com regras por descritivo (por
  exemplo, as comissões e o imposto do selo), criando o lançamento por
  conferir já conciliado.
- **F3-19C**: conciliar um movimento com vários lançamentos (um pagamento de
  várias faturas) e vários movimentos com um lançamento.
- Open Banking (PSD2), mais tarde.
