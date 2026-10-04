# ADR-0008 — Comunicação com a AT: certificado e camada de transporte

- Estado: proposto para a F2-02
- Data: 2026-09-09

## Contexto

O ponto 3 da visão pede a comunicação direta com a AT (IVA periódico, IES, IRC,
consulta e-Fatura). O primeiro requisito técnico, comum a tudo, é a **ligação
autenticada por certificado**: a AT identifica o produtor/integrador na ligação
SOAP por um certificado de transporte (PFX com chave privada, protegido por
senha) — ver [`../CERTIFICACAO_AT.md`](../CERTIFICACAO_AT.md).

A adesão do NexGen Accounting AI ao webservice como analisador autónomo **não está
garantida** (a AT enquadra a adesão nos produtores de faturação) — é um bloqueio
externo. Mas a plataforma pode e deve ter já a configuração e o carregamento do
certificado, para os testes usarem o certificado do PHC.

Riscos a fechar: a **senha do certificado** e a **chave privada** nunca podem
estar em texto, no repositório, nem em log; a configuração tem de distinguir
**testes** de **produção**; e a camada de transporte tem de ficar isolada (vai
precisar de `System.ServiceModel.*` quando as chamadas SOAP entrarem).

## Decisão

- **Configuração (secção `At`):**
  - `At:Certificate:Directory` e `At:Certificate:FileName` — o **local e o nome**
    do ficheiro PFX/P12. (Um `At:Certificate:Path` absoluto, se presente,
    tem prioridade.)
  - `At:Certificate:Password` — nunca em log nem devolvida por um *endpoint*.
    Guardada **cifrada** (ver abaixo); em alternativa, `AT_CERTIFICATE_PASSWORD`.
  - `At:Environment` — `Testes` (por omissão) ou `Producao`.
  - Os ficheiros `*.pfx`/`*.p12` já estão no `.gitignore`.
- **Valores de configuração cifrados** — qualquer valor de `At:Certificate:*`
  pode ir cifrado, com o prefixo `enc:v1:…`; o `AtSecretProtector` decifra-o ao
  ler (um valor sem prefixo é usado tal como está, para a migração ser gradual).
  A cifra é a Data Protection do ASP.NET Core (sem pacote novo — vem do
  *framework*), com o chaveiro em `%LOCALAPPDATA%\AccountingAI\dp-keys`
  protegido pelo DPAPI do Windows: os valores só se decifram na mesma máquina e
  conta. Uma ferramenta de linha de comandos
  (`... -- protect-at-secret "<valor>"`) produz o valor cifrado. Assim, mesmo o
  que fica num ficheiro de configuração ou em *user-secrets* não está em texto.
- **`IAtCertificateProvider`** (fronteira em `Application`) — carrega o
  `X509Certificate2` do local configurado com a senha e devolve o
  `AtCertificateStatus`: `Configured`, `Valid`, `Subject`, `Issuer`,
  `SerialNumber`, `Thumbprint`, `NotBeforeUtc`, `NotAfterUtc`, `HasPrivateKey`,
  `Environment`, e `Problem` (texto pt-PT) quando algo falha (ficheiro
  inexistente, senha errada, expirado). **Nunca** expõe a senha nem os bytes da
  chave.
- **Projeto novo `NexGenAccountingAI.Infrastructure.At`** — só referencia
  `Application` (e, mais tarde, os pacotes de `System.ServiceModel` para o
  cliente SOAP). É o único sítio que carrega o certificado e, no futuro, fala
  com a AT. Entra no `.slnx`, na referência da API e no teste de arquitetura.
  `AddAccountingAt(configuration)` liga as opções e regista o *provider*.
- **API:** `GET /api/v1/development/at/certificate` — o `AtCertificateStatus`
  (sem segredos). `Configured = false` quando não há ficheiro configurado.
- **PWA:** um ecrã **«Comunicação com a AT»** (`/comunicacao-at`) que mostra o
  estado do certificado (assunto, validade, impressão digital, se tem chave
  privada, ambiente) e explica onde se configura. O local e a senha **não** são
  editáveis pela interface — a senha é um segredo do lado do servidor.
- **Fora do âmbito desta fase:** as chamadas SOAP à AT (consulta e-Fatura,
  submissão de declarações) — dependem da aceitação da adesão pela AT e dos
  elementos de teste; entram em sub-incrementos seguintes quando esses
  elementos existirem. Os subutilizadores/perfis WFA por sujeito passivo também.

## Consequências

- A plataforma fica pronta para os testes com o certificado do PHC: aponta-se a
  configuração para o ficheiro, põe-se a senha em *user-secrets*, e o ecrã
  confirma que o certificado carrega e está válido.
- O segredo (senha) e a chave privada ficam contidos: uma fronteira, um
  projeto, nunca na interface nem em log.
- Quando a adesão à AT estiver resolvida, o cliente SOAP assenta neste
  *provider* (que já entrega o `X509Certificate2` pronto) em vez de recomeçar.
- Mais um projeto e mais uma linha no teste de arquitetura — o custo de manter a
  fronteira, aceite como em `Infrastructure.Ai` (ADR-0006).
