# Arquitetura

## Topologia durante a Fase 1

A solução contém agora duas fronteiras independentes:

1. a nova plataforma web, constituída por `AccountingAI.Domain`, `AccountingAI.Application`, `AccountingAI.Infrastructure.SqlServer`, `AccountingAI.Infrastructure.Files`, `AccountingAI.Api`, `AccountingAI.Worker` e `AccountingAI.Web`;
2. o monitor existente, constituído por `AccountingAI.Core` e `AccountingAI.WinForms`.

A nova plataforma segue um monólito modular e não referencia os projetos legados nesta fase. Esta separação impede que dependências específicas de Windows, WebView2, ODBC ou PHC condicionem o futuro núcleo contabilístico multiplataforma. As decisões estão registadas em `docs/architecture-decisions` e as dependências são verificadas por `AccountingAI.ArchitectureTests`.

`AccountingAI.Infrastructure.SqlServer` contém migrações versionadas e regista readiness através de uma ligação real à base configurada. O schema `organization` contém os gabinetes e empresas, incluindo a região fiscal `Continente`, `Açores` ou `Madeira`; o schema `infrastructure` mantém o estado técnico. A aplicação automática de migrações e os dados demonstrativos estão ativos apenas no perfil local de desenvolvimento; nos restantes ambientes são operações explícitas.

O schema `identity` contém utilizadores, associações a empresas e configurações por par utilizador–empresa. A configuração depende por chave estrangeira composta de `CompanyUsers(CompanyId, UserId)`, pelo que a base rejeita preferências para uma empresa à qual o utilizador não esteja associado. Todas as entidades contabilísticas futuras que pertençam a uma empresa deverão conter `CompanyId` e ser consultadas apenas através de um contexto de empresa explícito.

O schema `accounting` inicia o núcleo contabilístico com contas, diários, tipos de documento, movimentos e linhas. As chaves estrangeiras compostas por `CompanyId` impedem que um movimento use um diário, tipo de documento ou conta de outra empresa. O domínio só conclui movimentos com pelo menos duas linhas e débitos iguais aos créditos. Cada empresa declara a norma contabilística e recebe uma cópia editável de um catálogo base versionado; o provisionamento cria contas, diários e tipos de documento numa só transação e pode ser repetido sem duplicar códigos. Em `Development`, o seed idempotente cria dados exclusivamente sintéticos para duas empresas; a API confirma a associação ativa do utilizador e filtra sempre por `CompanyId` antes de devolver esses movimentos à PWA.

As contas de movimento podem guardar `TaxonomyReference` e `TaxonomyCode`. O domínio impede taxonomias em contas agregadoras, exige referência e código em conjunto e aplica a regra especial do referencial `O`. O catálogo da empresa determina `S`, `M` ou `N` a partir da norma; a interface apresenta a cobertura sem inventar classificações para contas ainda não validadas.

`AccountingMasterDataService` mantém o plano de contas de cada empresa: cria contas próprias com conta-mãe agregadora validada, renomeia, ativa/desativa (recusando desativar contas com linhas de movimento ou subcontas ativas) e atribui, limpa ou reaplica em bloco o código de taxonomia. `TaxonomyReviewService` constrói a cobertura das contas de movimento a partir da norma e do plano base versionado, classificando cada conta como atribuída, com sugestão, por atribuir ou com referencial diferente, sem reproduzir um catálogo legal completo. Ambos operam sempre por `CompanyId` e em transação. Os diários e os tipos de documento continuam só de consulta.

Na nova plataforma, as entradas previstas são ficheiros SAF-T, e-Fatura e extratos bancários por ficheiro. `AccountingAI.Infrastructure.Files` é o adaptador de ficheiros e depende apenas de `Application` e `Domain`. `SaftHeaderReader` lê o cabeçalho de um SAF-T (PT) com `XmlReader` (DTD proibido, `XmlResolver` desativado, entidades limitadas), materializando só o subárvore `Header`, agnóstico ao namespace e com suporte de Windows-1252. `AccountingAI.Domain.Saft.SaftHeader` é o valor de leitura validado e `ISaftHeaderReader` a fronteira em `Application`. `SaftInspectionService`, em `Infrastructure.SqlServer`, compõe o leitor com a base para encontrar uma empresa acessível com o mesmo NIF ou propor a criação, sem escrever. O monitor WinForms conserva integrações históricas, incluindo PHC, mas essas dependências não passam para os novos módulos. Uma futura integração com ERP deverá ser um adaptador independente e substituível.

## Componentes

- `AccountingAI.Core`: leitura XML, modelos tabulares, validações e contratos de integração.
- `AccountingAI.WinForms`: interface Windows, pesquisa, detalhe e importação de ficheiros.
- `AccountingAI.Smoke`: validação repetível contra os exemplos disponíveis.

## Estratégia de leitura

O parser usa `XmlReader` com DTD proibido e `XmlResolver` desativado. Cada registo relevante é materializado isoladamente; não é carregado todo o XML num `XDocument`. Os campos folha são convertidos numa representação tabular dinâmica usando o respetivo caminho, pelo que campos opcionais ou extensões presentes num ficheiro não são silenciosamente eliminados.

Os seguintes registos são tratados como unidades:

- `Header`;
- `Account`, `Customer`, `Supplier`, `Product`, `TaxTableEntry`;
- `Journal`, `Transaction`, `DebitLine`, `CreditLine`;
- `Invoice`, `StockMovement`, `WorkDocument`, `Payment` e respetivas `Line`.

As chaves XML originais permanecem no modelo de dados para garantir relações, pesquisa e rastreabilidade. Na interface, `SaftFieldLabels` converte os cabeçalhos e nomes dos campos para Português Europeu; a tradução é exclusivamente de apresentação e não altera o SAF-T carregado.

O tipo declarado em `TaxAccountingBasis` é mostrado, mas as capacidades da interface são ativadas pelo conteúdo efetivamente encontrado. Isto permite abrir ficheiros parciais ou ficheiros cujo conteúdo não coincide totalmente com a classificação esperada.

## e-Fatura

`IEfaturaPurchaseSource` define a fronteira para fontes de compras. `EfaturaCsvImporter` agrega vários lotes CSV e assinala ficheiros acima de 300 registos. `AtEfaturaPurchaseSource` implementa a consulta oficial como adquirente:

- intervalo convertido em pedidos diários;
- páginas de 300 documentos, incluindo dias com mais de uma página;
- autenticação WS-Security com AES-128/ECB/PKCS7 e nonce RSA;
- certificado TLS de produtor;
- ambientes de testes e produção;
- documentos e resumos de linhas relacionados por chave composta.

A senha normal do Portal pode ser persistida por empresa; a senha do PFX é recolhida para cada consulta e mantida apenas durante a operação. A integração SOAP só deve ser considerada validada depois de um teste autorizado com certificado e credenciais WFA reais.

`EfaturaPortalDialog` oferece uma via assistida separada do webservice: usa WebView2 com perfil próprio, desativa a gravação de palavras-passe e mantém a autenticação, os cookies e os mecanismos CSRF dentro do contexto oficial do browser. No seletor inicial da autenticação AT, escolhe automaticamente o separador `NIF`; o texto da alternativa `CC/CMD` não é suficiente para classificar a página como desafio. Só são considerados desafios os controlos efetivos de OTP/CAPTCHA ou mensagens que peçam a introdução de um código. O formulário NIF é preenchido por `Input.insertText` através do protocolo de desenvolvimento do próprio WebView2, produzindo os eventos de entrada que o estado JavaScript da página exige, e é submetido automaticamente. O fluxo divide o intervalo através de `EfaturaPortalExtractionPlan`. Em cada dia, desativa os campos visuais de calendário e acrescenta ao mesmo formulário campos ocultos `dataInicioFilter`/`dataFimFilter` em `yyyy-MM-dd`; a submissão nativa evita conversões de localização como `08/25/2026`. A exportação só começa depois do evento `NavigationCompleted`, impedindo a reutilização acidental dos resultados do dia anterior. O estado inclui cancelamento, expiração da sessão, falha de autenticação, validação de datas, ausência de resultados, timeout e erro de download/importação. Um CSV com 300 registos gera um aviso de possível truncagem.

Esta via depende da interface Web não documentada do Portal e dos respetivos elementos visuais; não tenta reproduzir diretamente cookies ou endpoints internos. Não contorna CMD, autenticação multifator ou CAPTCHA. Deve ser validada numa sessão real autorizada sempre que a AT alterar o portal, sem registar as credenciais em logs.

`ConfigurationStore` persiste a configuração em `%LOCALAPPDATA%\Accounting AI\config.dat`. O documento JSON completo é protegido por DPAPI para o utilizador atual do Windows antes de ser escrito, incluindo empresas, associações por NIF, senha normal do Portal e credenciais ODBC. A gravação usa um ficheiro temporário e substituição atómica. Uma configuração existente que não possa ser desencriptada é preservada e não é substituída silenciosamente. A senha do PFX continua fora do modelo. Todas as integrações AT do produto são de leitura; não existem operações de escrita ou comunicação de documentos.

`GridLayoutManager` atribui uma chave estável a cada grelha funcional e guarda a largura pelo identificador interno da coluna. Na primeira apresentação calcula larguras a partir das células visíveis; depois muda para modo manual, restaura preferências existentes e grava alterações com atraso curto para não escrever o ficheiro repetidamente durante o arrastamento. O gestor copia estas preferências quando a configuração geral é substituída por uma cópia editada no diálogo.

## PHC

`PhcCompanyCatalogReader` lê `EP.DBF` sem o alterar e extrai `NOME`, `DATAB` e `ODBC`, ignorando registos marcados como eliminados. `PhcCompanyCatalogSynchronizer` atualiza o cadastro local em cada arranque, preservando os NIF já associados e as credenciais. A localização inicial de desenvolvimento é `C:\PHC_LOC`, mas permanece configurável.

Como o `EP.DBF` não contém NIF, `PhcCompanyAssociationDialog` solicita a correspondência na primeira abertura de um SAF-T desconhecido. A associação passa depois a ser automática por `Header/TaxRegistrationNumber`; o mesmo NIF seleciona imediatamente a empresa ao abrir a configuração. As credenciais são guardadas por DSN ODBC; a primeira introdução só é aceite depois de `PhcOdbcDataSource` abrir a base e executar `SELECT 1`, sendo o resultado sempre comunicado ao utilizador.

`PhcOdbcDataSource` lê a lista de clientes de `CL` usando `no`, `nome` e `ncont`. A configuração por empresa mantém as opções de criação de clientes, criação de artigos, movimentação de stocks (dependente da criação de artigos), cliente genérico e senha do e-Fatura.

`SaftSeriesExtractor` identifica séries pelo prefixo anterior ao último `/`, separando o tipo SAF-T quando este aparece no número. A seleção é limitada aos ficheiros cujo `Header/TaxRegistrationNumber` coincide com o NIF da empresa PHC. `PhcSeriesMappingsDialog` relaciona estas séries com os catálogos read-only obtidos de `TD` e `TSRE`; `RETD` é aceite como fallback de compatibilidade. No lançamento agregado, as séries FT têm de apontar para um único documento `TD`.

`SaftVatRateExtractor` combina as taxas de IVA de `TaxTable` e das linhas de faturação, limitadas ao NIF da empresa. `PhcOdbcDataSource` lê os artigos `ST`. `PhcCompanyConfiguration` guarda o modo de lançamento e a correspondência taxa de IVA/artigo; a interface só exige essa correspondência nos dois modos que agrupam linhas.

`PhcAggregatedInvoiceBuilder` implementa o primeiro modo: exclui anulados, calcula valores assinados (incluindo notas de crédito), agrega as linhas por taxa e reconcilia o IVA calculado com `DocumentTotals/TaxPayable` de cada documento. Produz também uma referência determinística para idempotência.

`PhcAggregatedInvoiceService` executa três fases explícitas:

1. valida cliente, documento `TD`, artigos `ST`, `CPOC`, tabelas e taxas `TAXASIVA`, sem escrever;
2. entrega o resultado a uma pré-visualização reconciliável;
3. depois da confirmação, cria `FT`, `FT2`, `FT3`, `FI` e `FI2` numa transação serializável, verifica as linhas e faz rollback integral em caso de erro.

A idempotência é verificada antes e dentro da transação através da referência guardada em `FT.FREF`. O escritor deteta as colunas realmente existentes em cada tabela e rejeita a operação quando faltam chaves obrigatórias. Nesta fase recusa documentos `TD` que movimentem stocks, conta corrente ou contabilidade automática, porque essas operações exigem tabelas adicionais.

As configurações de documentos, séries, armazéns e tabelas PHC variam por instalação. Devem ser lidas e confirmadas no ambiente real, em vez de serem codificadas como constantes universais.
