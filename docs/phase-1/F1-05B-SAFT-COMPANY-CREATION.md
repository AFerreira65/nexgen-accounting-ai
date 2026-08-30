# F1-05B — Criar a empresa a partir do SAF-T e provisionar os cadastros

## Objetivo

Segundo passo do fluxo de importação: quando o NIF do ficheiro não corresponde a
nenhuma empresa acessível, propor a criação e, na mesma transação, provisionar os
cadastros contabilísticos. Continua sem ler clientes, contas ou movimentos do
ficheiro.

Ficam disponíveis:

- confirmar designação, região fiscal, norma contabilística e moeda antes de criar;
- criar a empresa e provisionar plano de contas, diários e tipos de documento numa só transação;
- associar o utilizador atual à empresa criada;
- recusa explícita de duplicados: se já existir uma empresa com o mesmo NIF, a operação falha sem escrever.

## Regras

`SaftCompanyCreationService` (em `Infrastructure.SqlServer`) compõe
`CompanyAccountingProvisioner` com a base:

1. deriva o gabinete das associações ativas do utilizador; sem gabinete, recusa;
2. abre uma transação e verifica que **não** existe empresa com o mesmo NIF — se existir, lança e nada é escrito;
3. cria a `Company` (o construtor valida NIF, região, norma e moeda);
4. provisiona os cadastros pela via de transação existente do `CompanyAccountingProvisioner`, reutilizando o catálogo base versionado;
5. cria a associação `CompanyUser` com a função `Accountant`;
6. confirma e devolve as contagens de contas, diários e tipos de documento provisionados.

A norma fica bloqueada após o primeiro provisionamento, como em qualquer empresa.
Um SAF-T real nunca cria uma empresa em silêncio: a criação é sempre uma ação
confirmada pelo utilizador.

## Superfície de validação local

Endpoint só do ambiente `Development`:

```text
POST /api/v1/development/users/{userId}/saft/companies
```

Corpo: `taxRegistrationNumber`, `legalName`, `taxRegion` (`Mainland`, `Azores` ou
`Madeira`), `accountingStandard` e `baseCurrency` opcional. A resposta traz a
empresa criada e as contagens provisionadas. Região ou norma desconhecidas,
duplicados e utilizador inexistente devolvem `ValidationProblem`/`404` sem
escrever.

Na PWA, o ecrã **Importar SAF-T** apresenta um formulário de criação quando a
correspondência devolve *proposta de criação*, com a designação e a moeda
pré-preenchidas pelo cabeçalho. A criação exige ligação ao SQL Server; em modo de
demonstração o botão fica desativado. Após criar, o ecrã mostra o resumo e indica
que a empresa fica disponível no seletor ao recarregar.

## Validação efetuada

- build .NET Release sem avisos nem erros;
- teste de integração SQL Server LocalDB: criação de uma empresa a partir de um NIF sintético, com contagem de contas/diários/documentos igual ao catálogo da norma, associação do utilizador criada e recusa de um segundo pedido com o mesmo NIF;
- teste de API para a recusa de duplicados, região inválida e utilizador inexistente, sem escrever na base;
- lint, nove testes de interface e build de produção da PWA;
- `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências.

A cobertura do caminho de escrita fica no teste de integração, que usa uma base
LocalDB descartável; os testes de API mantêm-se de leitura sobre a base
configurada.

## Fora deste incremento

- leitura de clientes, fornecedores, contas, taxas, diários, movimentos ou linhas do ficheiro;
- pré-visualização reconciliável e gravação de qualquer dado do ficheiro;
- atualização automática do contexto de trabalho na PWA sem recarregar;
- escolha de gabinete quando o utilizador pertence a mais do que um;
- validação XSD/SVAT oficial.

O próximo incremento deve começar a ler o conteúdo do ficheiro — plano de contas e
tabelas mestre — e apresentá-lo em pré-visualização, antes de qualquer gravação de
movimentos.
