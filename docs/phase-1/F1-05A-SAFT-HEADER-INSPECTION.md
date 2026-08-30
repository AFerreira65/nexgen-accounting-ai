# F1-05A — Inspeção do cabeçalho SAF-T e correspondência de empresa

## Objetivo

Primeiro passo do fluxo de importação contabilística definido na F1-04A: dado um
ficheiro SAF-T (PT), ler e validar o cabeçalho e descobrir a que empresa
pertence, pelo NIF. Este incremento **não** cria empresas nem importa lançamentos.

Ficam disponíveis:

- um leitor de cabeçalho SAF-T na nova plataforma, independente do monitor WinForms;
- correspondência do ficheiro a uma empresa acessível pelo `TaxRegistrationNumber`;
- um ecrã **Importar SAF-T** que apresenta o cabeçalho lido e o resultado da correspondência.

## Novo módulo: `AccountingAI.Infrastructure.Files`

A arquitetura já previa entradas por ficheiro (SAF-T, e-Fatura, extratos
bancários). Este módulo é o adaptador de ficheiros da nova plataforma. Depende
apenas de `Application` e `Domain`, tal como `Infrastructure.SqlServer`, e é
verificado por `AccountingAI.ArchitectureTests`. Não referencia `AccountingAI.Core`.

`SaftHeaderReader` usa `XmlReader` com DTD proibido, `XmlResolver` desativado e
`MaxCharactersFromEntities = 0`, e materializa apenas o subárvore `Header`; o resto
do ficheiro não é lido. A leitura é agnóstica ao namespace (por nome local) e
suporta a codificação oficial Windows-1252 além de UTF-8. Ficheiros de dezenas de
MB são aceites porque o leitor pára em `</Header>`.

## Domínio e fronteira

- `AccountingAI.Domain.Saft.SaftHeader` — valor de leitura validado: versão, NIF, `TaxAccountingBasis`, nome, exercício, período, moeda e campos opcionais. Normaliza NIF e moeda, exige período coerente e expõe `IncludesAccounting` (verdadeiro para `C` e `I`).
- `AccountingAI.Application.Saft.ISaftHeaderReader` — fronteira de leitura; `SaftFileFormatException` para conteúdo ilegível.
- `SaftInspectionService` (em `Infrastructure.SqlServer`) compõe o leitor com a base: procura uma empresa ativa com o mesmo NIF a que o utilizador tem associação ativa. Devolve `ExistingCompany` (com norma e versão de configuração) ou `NewCompanyProposed`. Não escreve nada.

## Superfície de validação local

Endpoint só do ambiente `Development`:

```text
POST /api/v1/development/users/{userId}/saft/inspect
```

O corpo do pedido é o ficheiro SAF-T. A resposta traz o cabeçalho e a
correspondência de empresa. Cabeçalhos ilegíveis devolvem `ValidationProblem` em
Português; um `userId` desconhecido devolve `404`. O limite de tamanho do corpo é
levantado só para este pedido, embora o leitor consuma apenas o cabeçalho.

Na PWA, o ecrã **Importar SAF-T** lê o ficheiro pela API quando ligada ao SQL
Server. Sem API, extrai o cabeçalho no browser e faz a correspondência apenas
contra as empresas de demonstração, identificando o modo. Os passos seguintes —
confirmar norma e referencial, provisionar os cadastros, pré-visualizar e importar
— continuam por implementar e o ecrã di-lo explicitamente.

## Validação efetuada

- build .NET Release sem avisos nem erros; `AccountingAI.ArchitectureTests` confirma que o novo módulo só depende de `Application` e `Domain` e não toca no legado;
- testes de domínio para o `SaftHeader` (normalização, campos obrigatórios, período, exercício);
- sete testes do `SaftHeaderReader`: cabeçalho de contabilidade e de faturação, com e sem namespace, Windows-1252, NIF em falta, conteúdo não-SAF-T e período inválido;
- teste de API para a correspondência a uma empresa existente, a proposta de criação para NIF desconhecido, ficheiro ilegível e utilizador inexistente;
- lint, oito testes de interface e build de produção da PWA;
- `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências.

Os ficheiros SAF-T reais em `SAFTs Exemplo/` não são versionados e não foram
copiados; as fixtures de teste são sintéticas.

## Fora deste incremento

- criação da empresa a partir do SAF-T;
- confirmação de norma/referencial e provisionamento dos cadastros no contexto da importação;
- leitura de clientes, fornecedores, contas, diários, movimentos ou linhas do ficheiro;
- pré-visualização reconciliável e gravação de qualquer dado do ficheiro;
- validação XSD/SVAT oficial;
- e-Fatura e extratos bancários por ficheiro.

O próximo incremento deve continuar o fluxo: quando o NIF não corresponde a
nenhuma empresa, propor a criação (nome, região fiscal, norma, referencial) e
provisionar os cadastros na mesma transação, sem nunca criar uma empresa
duplicada em silêncio.
