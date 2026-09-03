# Accounting AI

Monitor Windows em C#/.NET para leitura e análise de ficheiros SAF-T (PT).

## Nova plataforma contabilística — F1-23 abertura do exercício

Sobre a fundação F1-01 foi criada a primeira experiência contabilística local:

- monólito modular .NET 10 (`Domain`, `Application`, `Infrastructure.SqlServer`, `Api` e `Worker`);
- PWA React/TypeScript;
- SQL Server com migrações versionadas;
- health checks, correlação e logs estruturados;
- cadastro base de gabinete e empresa em SQL Server local;
- base `accountingAI` em SQL Server Express, criada e atualizada por migrações no arranque;
- utilizadores associados a uma ou mais empresas, com função e preferências próprias em cada empresa;
- seletor de empresa ligado à API e ecrã para editar preferências próprias do utilizador na empresa ativa;
- plano de contas, diários, movimentos e linhas contabilísticas persistidos no schema `accounting`;
- norma contabilística escolhida por empresa, com plano de contas base versionado;
- provisionamento transacional e idempotente de contas, diários e tipos de documento ao criar a empresa;
- ecrãs pesquisáveis de plano e abertura de contas, diários, documentos contabilísticos e taxonomias;
- criação de contas, diários e tipos de documento próprios da empresa, renomeação e ativação/desativação, com guardas contra desativar registos com movimentos, subcontas ativas ou usados como predefinidos;
- atribuição, limpeza e aplicação em bloco do código de taxonomia nas contas de movimento, com mapa de revisão de cobertura;
- catálogo completo de taxonomias por referencial (Taxonomia S e M, transcritas da Portaria 302/2016), com classificação automática apenas das contas inequívocas;
- controlo de cobertura `TaxonomyReference`/`TaxonomyCode` por norma e por conta de movimento;
- dados sintéticos idempotentes e distintos para as empresas do Continente e dos Açores;
- leitura do cabeçalho de um ficheiro SAF-T (PT) na nova plataforma e correspondência da empresa pelo NIF;
- criação da empresa a partir do SAF-T quando o NIF não corresponde a nenhuma, com provisionamento dos cadastros na mesma transação e recusa de duplicados;
- ecrã de organização para criar e editar empresas e utilizadores e gerir associações;
- ecrã de lançamento de movimentos com linhas repetíveis, totais e validação de equilíbrio, e ações para conferir, corrigir e anular;
- balancete, razão de conta e livro diário, filtrados por empresa, diário, tipo de documento, exercício e intervalo de datas, sem os lançamentos anulados, com saldo de abertura por conta no balancete e na razão quando há data inicial, agrupamento opcional do balancete por classe SNC com subtotais, e exportação CSV do que está no ecrã;
- numeração dos lançamentos por tipo de documento, além da numeração por diário, com o tipo persistido no lançamento e filtro de movimentos por tipo, e um contador persistido por empresa, série e exercício;
- emissão simultânea protegida no SQL Server, sem referências duplicadas, e ecrã de controlo das séries com último número, próximo número e avanço seguro sem recuos;
- demonstração de resultados sintética por naturezas (classe 7 menos classe 6, por subclasse, com resultado líquido do período) e balanço sintético (ativo, passivo e capital próprio por subclasse, com o controlo Ativo = Passivo + Capital próprio), ambos a partir do balancete;
- apuramento de resultados do exercício (fecha as classes 6 e 7 contra a conta 81) e abertura do exercício seguinte (transporta os saldos de balanço e leva o resultado a 56), cada um uma vez por exercício e no respetivo diário;
- dashboard, movimentos, razão de conta (com saldo de abertura e movimentos da API) e diário alimentados pela API e filtrados pela empresa ativa;
- painel de trabalho apelativo e responsivo;
- navegação contextual entre movimento, conta e diário;
- testes de arquitetura, domínio, API, migrações e frontend.

Esta evolução não altera o comportamento do monitor WinForms descrito abaixo. Os movimentos sintéticos de desenvolvimento continuam presentes; além disso, a aplicação regista, confere, corrige e anula lançamentos. O plano de contas, os diários, os tipos de documento e as taxonomias já são editáveis. A importação de SAF-T lê e valida o cabeçalho, faz a correspondência da empresa e, quando o NIF não corresponde a nenhuma, cria a empresa e provisiona os cadastros; ainda não lê contas nem movimentos do ficheiro. Consulte [F1-02A](docs/phase-1/F1-02A-LOCAL-EXPERIENCE.md), [F1-02B](docs/phase-1/F1-02B-ENGINEERING-GATES.md), [F1-02C](docs/phase-1/F1-02C-MULTI-COMPANY-DATABASE.md), [F1-03A](docs/phase-1/F1-03A-WORK-CONTEXT.md), [F1-03B](docs/phase-1/F1-03B-ACCOUNTING-TEST-DATA.md), [F1-03C](docs/phase-1/F1-03C-ACCOUNTING-TEMPLATES.md), [F1-04A](docs/phase-1/F1-04A-ACCOUNTING-MASTER-DATA.md), [F1-04B](docs/phase-1/F1-04B-ACCOUNTING-MASTER-DATA-MAINTENANCE.md), [F1-05A](docs/phase-1/F1-05A-SAFT-HEADER-INSPECTION.md), [F1-05B](docs/phase-1/F1-05B-SAFT-COMPANY-CREATION.md), [F1-06A](docs/phase-1/F1-06A-JOURNAL-DOCUMENT-MAINTENANCE.md), [F1-06B](docs/phase-1/F1-06B-TAXONOMY-CATALOG.md), [F1-06C](docs/phase-1/F1-06C-ORGANIZATION-ADMIN.md), [F1-07](docs/phase-1/F1-07-MOVEMENT-POSTING.md), [F1-08](docs/phase-1/F1-08-MOVEMENT-REVIEW-VOID.md), [F1-09](docs/phase-1/F1-09-TRIAL-BALANCE-LEDGER.md), [F1-10](docs/phase-1/F1-10-DOCUMENT-TYPE-NUMBERING.md), [F1-11](docs/phase-1/F1-11-JOURNAL-ENTRY-DOCUMENT-TYPE.md), [F1-12](docs/phase-1/F1-12-JOURNAL-REPORT.md), [F1-13](docs/phase-1/F1-13-PERIOD-REPORTS.md), [F1-14](docs/phase-1/F1-14-ACCOUNT-LEDGER-SCREEN.md), [F1-15](docs/phase-1/F1-15-REPORT-CSV-EXPORT.md), [F1-16](docs/phase-1/F1-16-TRIAL-BALANCE-OPENING.md), [F1-17](docs/phase-1/F1-17-DOCUMENT-NUMBER-SERIES.md), [F1-18](docs/phase-1/F1-18-DOCUMENT-SERIES-CONTROL.md), [F1-19](docs/phase-1/F1-19-TRIAL-BALANCE-BY-CLASS.md), [F1-20](docs/phase-1/F1-20-INCOME-STATEMENT.md), [F1-21](docs/phase-1/F1-21-BALANCE-SHEET.md), [F1-22](docs/phase-1/F1-22-YEAR-END-CLOSING.md) e [F1-23](docs/phase-1/F1-23-YEAR-OPENING.md).

### Abrir a aplicação web local

Execute `Abrir Accounting AI.cmd` na pasta principal, ou:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\start-local.ps1
```

A aplicação abre em `http://127.0.0.1:5173/`. O ficheiro `src/AccountingAI.Web/index.html` não deve ser usado como aplicação através de `file://`; se for aberto diretamente, apresenta agora a instrução correta em vez de uma página branca.

### SQL Server Express local

No perfil de desenvolvimento, a aplicação aplica automaticamente as migrações em falta, cria as tabelas na base `accountingAI` e repõe de forma idempotente os dados sintéticos em falta. A configuração versionada usa autenticação integrada. Quando for necessário login SQL, guarde a ligação apenas no secret store local:

```powershell
dotnet user-secrets set "ConnectionStrings:Accounting" "<connection-string-local>" --project .\src\AccountingAI.Api\AccountingAI.Api.csproj
```

Não coloque utilizadores, passwords ou connection strings com credenciais em `appsettings*.json`, Git ou scripts.

## Estado atual

O MVP lê ficheiros `1.03_01` e `1.04_01` pelos nomes dos elementos XML, sem depender de um namespace fixo. Suporta ficheiros de faturação (`F`), parciais (`P`), contabilidade (`C`) e integrados (`I`), bem como as secções específicas de recibos, transporte e documentos de conferência.

Funcionalidades disponíveis:

- abertura de um ou vários ficheiros XML;
- abertura de todos os XML de uma pasta;
- arrastar e largar ficheiros ou pastas;
- leitura segura, com DTD e resolução externa desativadas;
- suporte da codificação oficial Windows-1252;
- grelhas pesquisáveis e painel de detalhe para todos os campos encontrados;
- redimensionamento manual das colunas das grelhas, com reposição automática das larguras na abertura seguinte;
- relação mestre-detalhe: as linhas mudam com o documento onde está o cursor;
- cabeçalhos e campos apresentados em Português Europeu, mantendo internamente as chaves SAF-T para relações e validação;
- tabelas mestre: clientes, fornecedores, produtos e impostos;
- contabilidade: plano de contas, diários, documentos e lançamentos;
- importação simultânea de vários CSV do e-Fatura, identificando cada lote e o limite de 300 registos;
- acesso assistido ao Portal e-Fatura numa janela Edge integrada, com sessão isolada, autenticação com as credenciais guardadas, consultas diárias e importação automática de cada CSV descarregado;
- consulta direta das compras à AT, dia a dia e com páginas de 300 documentos;
- menu de configuração para AT, instalação PHC, empresas e credenciais ODBC;
- atualização das empresas PHC a partir do `EP.DBF` em cada arranque;
- associação inicial entre o contribuinte do SAF-T e a empresa PHC correspondente;
- configuração por empresa da senha do e-Fatura, criação de clientes/artigos, movimentação de stocks e cliente genérico PHC;
- correspondência por empresa entre séries de faturação/recebimentos do SAF-T e documentos PHC de `TD`/`TSRE` (com fallback `RETD`);
- modo de lançamento de faturas por empresa e correspondência entre taxas de IVA do SAF-T e artigos PHC de `ST`;
- lançamento transacional de um documento PHC agregado no cliente genérico, com pré-visualização e proteção contra duplicados;
- teste obrigatório do primeiro utilizador e palavra-passe ODBC, com indicação explícita de sucesso ou erro;
- leitura dos clientes `CL` (`no`, `nome`, `ncont`), privilegiando o NIF genérico `999999990` quando existe;
- configuração integralmente encriptada pela proteção do Windows para o utilizador atual;
- leitura conjunta de vários períodos;
- diagnóstico básico de cabeçalho e conteúdo detetado.

O separador **Validações** ainda não é um validador XSD/SVAT completo. Distingue erros de leitura, avisos e informação, mas não substitui o validador oficial da AT.

O enquadramento dos certificados para webservices, da certificação de programas de faturação e do SVAT está descrito em [docs/CERTIFICACAO_AT.md](docs/CERTIFICACAO_AT.md).

## Executar

```powershell
dotnet run --project .\src\AccountingAI.WinForms\AccountingAI.WinForms.csproj
```

Também é possível passar ficheiros ou uma pasta na linha de comandos:

```powershell
dotnet run --project .\src\AccountingAI.WinForms\AccountingAI.WinForms.csproj -- ".\SAFTs Exemplo"
```

## Compilar e validar

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

O comando compila a solução completa, executa os testes da fundação, os smoke tests existentes e valida a PWA. O smoke test legado usa os cinco SAF-T reais existentes em `SAFTs Exemplo`, uma amostra sintética integrada e um `EP.DBF` sintético. Quando `C:\PHC_LOC\EP.DBF` está disponível, valida também a leitura desse catálogo real sem o alterar.

## Compras do e-Fatura

A aplicação importa um ou vários CSV obtidos no e-Fatura. Cada ficheiro fica identificado como lote próprio; lotes acima de 300 registos originam um aviso sem impedir a leitura.

Existem três vias independentes:

1. **Importar CSV** — seleciona um ou vários ficheiros já descarregados.
2. **Portal e-Fatura (assistido)** — pede a data inicial e final, abre o portal numa janela Edge/WebView2 com um perfil exclusivo do Accounting AI, seleciona automaticamente o método de autenticação **NIF** e introduz/submete o NIF e a palavra-passe guardados para a empresa identificada pelo SAF-T. A simples presença da alternativa `CC/CMD` no seletor inicial não é tratada como um desafio. A introdução usa eventos reais do navegador, necessários no formulário moderno de autenticação da AT. Depois consulta e exporta cada dia isoladamente (início e fim iguais). Para evitar que o calendário transforme as datas segundo a localização do Windows, a pesquisa submete diretamente no formulário os campos `dataInicioFilter` e `dataFimFilter` em `yyyy-MM-dd`, aguarda a navegação concluída e só então exporta/importa o CSV. Se um dia tiver exatamente 300 registos, assinala-o para confirmação, pois a divisão diária não consegue garantir a completude quando o próprio dia atinge o limite do Portal.
3. **Consulta de compras na AT (webservice)** — consulta SOAP direta como adquirente, com paginação e certificados autorizados; não envia nem altera documentos.

O perfil do browser e os CSV descarregados são guardados em `%LOCALAPPDATA%\Accounting AI\e-Fatura`. A janela permite cancelar a extração e apagar os cookies da sessão. O programa desativa o armazenamento de palavras-passe pelo WebView2 e não tenta contornar CMD, autenticação multifator ou CAPTCHA; se a AT pedir uma dessas etapas, a operação é interrompida com uma indicação ao utilizador.

A automatização assistida usa a interface Web atual do Portal, que não é uma API pública e pode ser alterada pela AT sem aviso. Por isso, os seletores da página e o fluxo completo devem ser novamente validados numa conta autorizada sempre que a AT modificar o Portal. As palavras-passe não são escritas nos registos da aplicação.

Em **Configuração > Configuração geral e empresas PHC** podem ser guardados os valores usados por defeito. A configuração fica em `%LOCALAPPDATA%\Accounting AI\config.dat`, integralmente encriptada para o utilizador atual do Windows. A senha normal do Portal fica associada a cada empresa; a senha do PFX não é gravada e continua a ser pedida em cada consulta oficial.

A consulta direta usa a operação `InvoicesRequest` do serviço oficial `fatshareFaturas`, no modo adquirente (`CustomerTaxID`). O intervalo inicial é sugerido a partir dos SAF-T carregados e pode ser alterado. O cliente divide o intervalo em dias e percorre todas as páginas de 300 documentos de cada dia. A AT exige:

- credenciais do sujeito passivo;
- certificado digital de produtor de software assinado pela AT;
- cabeçalho SOAP de segurança e cifra definidos pela AT;
- paginação e tratamento dos ambientes de teste e produção.

As senhas são introduzidas na janela da consulta, usadas apenas em memória e não são gravadas. O PFX e a chave pública da autenticação são carregados dos caminhos escolhidos pelo utilizador. Certificados, chaves e credenciais estão excluídos do controlo de versões.

O fluxo SOAP, AES/RSA, certificado cliente, segmentação diária, paginação e interpretação das respostas estão implementados e testados localmente com respostas sintéticas. A ligação ao ambiente da AT permanece **por validar ao vivo**, porque requer um certificado de produtor e credenciais WFA autorizadas. Desde 2025, os novos certificados de produtor devem observar a política RSA de 4096 bits publicada pela AT.

O Accounting AI utiliza estes serviços exclusivamente para consulta como adquirente. Não contém operações de registo, alteração, eliminação ou comunicação de documentos à AT.

## Empresas PHC

No arranque, a aplicação lê o `EP.DBF` da pasta PHC configurada. Na instalação de desenvolvimento é usada inicialmente `C:\PHC_LOC`; noutras instalações a localização pode ser alterada. O leitor aceita ainda uma pasta que contenha diretamente `EP.DBF` ou uma raiz com o ficheiro numa subpasta `_loc`.

Do `EP.DBF` são obtidos:

- nome da empresa (`NOME`);
- base de dados (`DATAB`);
- origem de dados ODBC (`ODBC`).

O `EP.DBF` não contém o contribuinte. Na primeira abertura de um SAF-T cujo NIF ainda não esteja associado, a aplicação pede ao utilizador que escolha a empresa PHC correspondente. Nas aberturas seguintes, a empresa é detetada automaticamente pelo NIF e fica imediatamente selecionada ao abrir o separador **Empresas PHC** da configuração.

As preferências de largura das colunas são guardadas por grelha e por campo dentro do mesmo `config.dat` encriptado. As grelhas novas começam com uma largura calculada pelo conteúdo visível; depois disso, cada ajuste feito com o rato passa a ser respeitado nas sessões seguintes.

As credenciais são mantidas por ODBC, permitindo que várias empresas que usem o mesmo DSN partilhem as mesmas credenciais. Na primeira introdução, a aplicação tenta abrir a base de dados e executar uma consulta mínima, informando sempre se o login foi aceite ou recusado; credenciais inválidas não são guardadas como validadas. Utilizador e palavra-passe ficam dentro do ficheiro encriptado e não aparecem em texto simples na grelha.

Por empresa podem ainda ser definidos **Cria Clientes**, **Cria Artigos** e, quando esta última opção está ativa, **Movimenta Stocks**. A lista de cliente genérico é obtida de `CL` pelos campos `no`, `nome` e `ncont`; se existir um cliente com `ncont = '999999990'`, é selecionado inicialmente.

O botão **Configurar faturação e recebimentos** apresenta duas grelhas. As séries são extraídas de `InvoiceNo`/`InvoiceType` e `PaymentRefNo`/`PaymentType` dos SAF-T pertencentes ao NIF da empresa. Os documentos de destino são lidos de `TD` (`NDOC`, `NMDOC`, `TIPOSAFT`) e de `TSRE` (`NDOC`, `NMDOC`, `TIPOREAO`); existe fallback para uma tabela `RETD` compatível. As linhas podem ser adicionadas ou removidas e as correspondências são guardadas por empresa no ficheiro encriptado. Os recebimentos continuam apenas configurados nesta fase.

O modo de lançamento de faturação também é definido por empresa: um único documento no cliente genérico, todos os documentos com linhas agrupadas por IVA, ou os documentos tal como constam do SAF-T. Nos dois modos agrupados surge uma grelha que relaciona cada taxa de IVA encontrada no SAF-T com um artigo lido de `ST` (`ref`, `design`).

Nesta fase, o botão **Lançar no PHC** executa apenas **Agrupa 1 documento no Cliente genérico**. A aplicação:

- ignora documentos anulados e agrega os restantes por taxa de IVA;
- usa a data do último documento do SAF-T e o cliente genérico configurado;
- valida o artigo `ST`, coloca `ST.CPOC` em `FI.CPOC` e `ST.TABIVA` em `FI.TABIVA`, e obtém `FI.IVA` de `TAXASIVA.TAXA` pelo respetivo código;
- apresenta empresa, documento, cliente, artigos, taxas e totais antes da confirmação;
- cria `FT`, `FT2`, `FT3`, `FI` e `FI2` numa única transação ODBC e faz rollback integral em caso de erro;
- grava uma referência determinística do conjunto SAF-T em `FT.FREF` e recusa novo lançamento do mesmo conjunto para o mesmo tipo de documento.

Por segurança, o documento `TD` de destino tem de estar configurado como importado/externo e, nesta primeira fase, não pode movimentar stocks, conta corrente ou integração contabilística automática. Essas áreas exigem as respetivas tabelas e regras PHC e permanecem fora deste lançamento inicial.

Nesta instalação os DSN PHC estão registados no ODBC de 32 bits, pelo que a publicação Windows é gerada para `win-x86`.

## Evolução prevista

1. Validação integral pelo XSD oficial e regras analíticas/SVAT.
2. Persistência de importações SAF-T de grande dimensão e análise multiperíodo sobre o núcleo contabilístico já iniciado.
3. Validação real da consulta direta no ambiente de testes e depois em produção da AT.
4. Cruzamento vendas SAF-T vs. compras e-Fatura vs. contabilidade.
5. Alargar o adaptador PHC aos outros dois modos, conta corrente, stocks, contabilidade e recebimentos, com regras explícitas por empresa.
6. Relatórios de gestão assistidos por IA, sempre construídos sobre dados reconciliados e com rastreabilidade até ao documento/lançamento.

Antes de usar o lançamento numa base real, deve ser feito um ensaio numa cópia de testes da base PHC e confirmadas a versão, as colunas obrigatórias, os `STAMP`, o documento `TD` e as regras específicas dessa instalação.
