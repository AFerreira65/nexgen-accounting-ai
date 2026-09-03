# F1-45 — Tamanho do texto ajustável nas preferências

## Objetivo

Deixar cada utilizador escolher, por empresa, um texto maior na interface —
pedido de utilização.

## Regras

`UserCompanySettings` ganha `TextSize` (`InterfaceTextSize`: `Normal`, `Large`,
`Larger`; por omissão `Normal`). `Update` passa a receber o valor e recusa
valores fora do enum.

O `PUT .../work-context/{userId}/companies/{companyId}/settings` aceita `textSize`
(`Normal`/`Large`/`Larger`, sem distinção de maiúsculas) e devolve-o na leitura do
contexto de trabalho e na resposta de gravação; um valor desconhecido devolve
`400`.

Na PWA, a página **Preferências de trabalho** ganha o campo **Tamanho do texto**.
O valor é aplicado ao elemento raiz (`data-text-size`) e amplia a área de
trabalho (`.workspace { zoom: 1.06 }` para *Grande*, `1.12` para *Maior*), sem
afetar a barra lateral nem as outras empresas.

## Alteração de esquema

Migração `20260903151623_UserCompanySettingsTextSize`: coluna `TextSize int NOT
NULL DEFAULT 1` em `identity.UserCompanySettings` (não destrutiva; as
configurações existentes ficam em `Normal`). Aplicada à base `accountingAI`.

## Superfície de validação local

Em **Definições**, mudar **Tamanho do texto** para *Grande* ou *Maior* e guardar
amplia imediatamente a interface; a leitura do contexto de trabalho inclui
`textSize`.

## Validação efetuada

- build .NET Debug sem avisos nem erros;
- teste de domínio: `Update` guarda `TextSize` e recusa um valor fora do enum;
- teste de integração SQL Server LocalDB: a migração consta das aplicadas; `Update(..., InterfaceTextSize.Larger)` persiste o valor;
- teste de API: `/foundation` indica a fase `F1-45`; o `PUT` de definições com `"textSize":"Gigante"` devolve `400` com «tamanho do texto»;
- verificação no browser contra a base `accountingAI`: mudar para *Maior* põe `data-text-size="larger"` e `zoom: 1.12` na área de trabalho; voltar a *Normal* repõe;
- teste de interface: mudar o campo e guardar altera o atributo `data-text-size` do root;
- suite .NET completa (65 testes), `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos;
- lint, trinta e um testes de interface e build de produção da PWA.

## Fora deste incremento

- escala independente da barra lateral;
- mais níveis ou um valor livre em pontos;
- lembrar a escolha sem ligação ao SQL Server entre sessões.
