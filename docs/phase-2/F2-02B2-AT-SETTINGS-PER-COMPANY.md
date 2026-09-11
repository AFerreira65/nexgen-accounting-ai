# F2-02B (continuação) — Periodicidade e ambiente da AT por empresa

Extensão de [F2-02B](F2-02B-AT-CREDENTIALS.md), a pedido do utilizador: as
definições da AT — não só as credenciais — devem ser por empresa, não só o
certificado (que é por servidor, F2-02A). Prepara o terreno para o envio
eletrónico da declaração periódica (F2-02C, ainda por especificar em
detalhe — ver «Fora deste incremento»).

## Regras

- **`Domain/Organizations/AtSettingsEnums`** — `VatFilingPeriodicity`
  (`Mensal`/`Trimestral`, artigo 41.º n.º 1 do CIVA) e `AtEnvironment`
  (`Testes`/`Producao`).
- **`CompanyAtCredentials`** ganha `Periodicity` e `Environment` (com
  omissão `Mensal`/`Testes`), guardados como `int`; migração aditiva
  `20260911180622_AtSettingsPerCompany` (duas `AddColumn` com
  `defaultValue: 0`). `Create`/`Update` passam a exigir os quatro campos
  juntos — não há, para já, um caminho para atualizar só a periodicidade
  sem voltar a indicar a senha (o mesmo que já acontecia antes desta
  extensão: o `PUT` sempre pede senha).
- **`CompanyAtCredentialsService`** — `CompanyAtCredentialsView` devolve
  `Periodicity`/`Environment` como texto (`"Mensal"`/`"Trimestral"`,
  `"Testes"`/`"Producao"`), não como número — a convenção já usada em
  `AccountVatClassificationRow` para os enums de IVA; um valor por omissão
  em ambos quando não há linha guardada.
- **API:** `PUT .../at-credentials` aceita agora `{ subUser, password,
  periodicity, environment }`; ambos opcionais no pedido (omissão →
  `Mensal`/`Testes`), `400` com uma mensagem específica quando o texto não é
  um dos valores válidos.
- **PWA:** o painel de `/comunicacao-at` passa a «Configuração da AT de
  <empresa>» — dois `<select>` novos (Periodicidade, Ambiente da AT) no
  mesmo formulário, e uma linha de resumo («Regime mensal, ambiente de
  testes.») quando configurado.

## Validação efetuada

- `AccountingAI.IntegrationTests`: `CompanyAtCredentialsServiceTests`
  estendido — guardar com `Trimestral`/`Producao` devolve-os como texto;
  atualizar substitui os valores guardados. `SqlServerMigrationTests` inclui
  a migração nova.
- `AccountingAI.Api.Tests`: `AtCertificateStatusIsReportedWithoutSecrets`
  passa a verificar as chaves `periodicity`/`environment` no `at-credentials`
  não configurado.
- `AccountingAI.Web`: `tsc`, `eslint` e os oitenta e três testes de
  interface verdes.
- `dotnet build`/`dotnet test` na solução (182 testes); `scripts/build.ps1
  -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências.
- **Gotcha apanhado e corrigido**: a primeira versão devolvia `Periodicity`/
  `Environment` como o valor `enum` diretamente no registo — o
  `System.Text.Json` por omissão serializa isso como número (`0`/`1`), não
  como o nome (`"Mensal"`/`"Testes"`), inconsistente com o resto da API e
  com o tipo TypeScript. Corrigido para `.ToString()` antes de devolver,
  como já se fazia em `AccountVatClassificationRow`; confirmado em direto
  (a resposta passou de `"periodicity":0` para `"periodicity":"Trimestral"`)
  antes de dar como concluído.
- Verificado em direto contra `accountingAI` (Vidreira Algarvia): guardado
  `Trimestral`/`Producao`, confirmado na resposta; guardado de novo
  `Mensal`/`Testes`, confirmado; `periodicidade`/`ambiente` inválidos deram
  `400` com a mensagem certa; painel na PWA mostrou os dois `<select>` e o
  resumo; linha removida a seguir, tabela confirmada vazia.

## Fora deste incremento

- o envio eletrónico da declaração periódica à AT (F2-02C) — **ainda por
  especificar em detalhe.** O utilizador confirmou que, numa primeira fase,
  o código deve **só validar, nunca submeter** (usando um utilizador de
  contribuinte real, mas sem efetivar nenhuma entrega). Antes de escrever
  esse código é preciso o contrato técnico do webservice da AT (WSDL ou
  documentação equivalente) — não se adivinham nomes de operação, formas de
  mensagem nem URLs de um webservice do Estado.
