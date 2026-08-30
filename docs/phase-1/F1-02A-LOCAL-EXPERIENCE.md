# F1-02A — Experiência local e base organizacional

## Estado

Implementada e validada localmente em 29 de agosto de 2026.

Esta tarefa resolve o arranque local da aplicação, estabelece a primeira linguagem visual do produto e cria a base organizacional mínima em SQL Server LocalDB. Não inicia outros épicos.

## Entregas

- lançador `Abrir Accounting AI.cmd`, que inicia API e PWA e abre o endereço HTTP correto;
- página explicativa quando `index.html` é aberto diretamente por `file://`, eliminando o ecrã branco sem fingir que a PWA funciona sem servidor;
- painel responsivo com identidade visual em verde-petróleo, superfícies claras e estados acessíveis;
- navegação contextual demonstrável `movimento → conta → movimentos relacionados` e `movimento → diário → movimentos do diário`;
- entidades de domínio `AccountingFirm` e `Company`, com NIF, moeda base e região fiscal para Continente, Açores e Madeira;
- migração repetível `202608290002_LocalOrganization` nos schemas `organization` e `infrastructure`;
- seed exclusivamente local de `Gabinete Horizonte` e `Almeida & Costa, Lda. (Demonstração)`;
- endpoint de desenvolvimento read-only `/api/v1/development/organization`.

## Executar

Na raiz do repositório, faça duplo clique em `Abrir Accounting AI.cmd` ou execute:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\start-local.ps1
```

Endereços:

- aplicação: `http://127.0.0.1:5173/`;
- API: `http://127.0.0.1:5080`;
- saúde: `http://127.0.0.1:5080/health/ready`.

## Aplicar a base local

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\migrate.ps1
```

A configuração de desenvolvimento usa autenticação integrada do Windows e a base `AccountingAI_Development` na instância `(localdb)\MSSQLLocalDB`. Não existem credenciais na configuração versionada.

## Critérios confirmados

- abrir o antigo endereço `file://` mostra uma orientação clara e acionável;
- o lançador abre a aplicação em HTTP sem exigir comandos manuais separados;
- o painel adapta-se a ecrãs desktop e móveis;
- um movimento permite abrir uma conta e o diário com um clique;
- uma conta e um diário permitem regressar aos movimentos relacionados;
- a migração pode ser aplicada duas vezes sem duplicar estruturas;
- build, análise estática, testes de domínio, arquitetura, API, SQL e frontend passam localmente.

## Limites

- os lançamentos contabilísticos apresentados na PWA são exemplos sintéticos e ainda não são gravados na base;
- não foram implementados autenticação, permissões, contabilização definitiva, SAF-T, e-Fatura, PHC, IA ou obrigações fiscais;
- LocalDB valida desenvolvimento neste computador; SQL Server de staging, produção e ambientes de cliente permanecem por validar;
- a região fiscal existe no cadastro base, mas as regras fiscais específicas das Ilhas serão introduzidas em tarefas próprias.
