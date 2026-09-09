# F2-02 — Comunicação com a AT

Épico da [Fase 2](README.md), ponto 3 da visão: a comunicação direta com a AT
(consulta e-Fatura, e, mais tarde, submissão de declarações). O primeiro passo,
comum a tudo, é a **ligação autenticada por certificado**. A decisão e as
fronteiras estão na
[`ADR-0008`](../architecture-decisions/ADR-0008-AT-COMMUNICATION.md); o
enquadramento legal está em [`../CERTIFICACAO_AT.md`](../CERTIFICACAO_AT.md).

## Contexto

- A AT identifica o produtor/integrador na ligação SOAP por um **certificado de
  transporte** — um PFX com chave privada, protegido por senha.
- A adesão do Accounting AI ao webservice como analisador autónomo **não está
  garantida** (bloqueio externo — precisa de confirmação escrita da AT ou de
  associação a um programa de faturação certificado). Os **elementos de teste**
  (chave pública do sistema de autenticação, endpoints de teste) também
  dependem desse pedido.
- O que **não** depende disso: ter já a configuração e o carregamento do
  certificado, para os testes usarem o **certificado do PHC**.

## Objetivo

- **Configuração do certificado** — o local e o nome do ficheiro PFX, e a senha
  (segredo do lado do servidor), mais o ambiente (testes / produção).
- **Estado do certificado** — carregar o certificado e mostrar o assunto, a
  validade, a impressão digital, se tem chave privada e o ambiente; sem nunca
  expor a senha nem a chave.
- **Base para o cliente SOAP** — um projeto isolado (`Infrastructure.At`) que
  entrega o `X509Certificate2` pronto; as chamadas à AT assentam nele quando os
  elementos de teste existirem.

Fora do âmbito desta fase: as chamadas SOAP à AT (consulta e-Fatura, submissão),
os subutilizadores/perfis WFA por sujeito passivo, e a Modelo 24.

## Desenho

### Application

```
Application/At/
  AtCertificateStatus.cs   // estado do certificado, sem segredos
  IAtCertificateProvider.cs
```

- `AtCertificateStatus(Configured, Valid, Subject, Issuer, SerialNumber,
  Thumbprint, NotBeforeUtc, NotAfterUtc, HasPrivateKey, Environment, Problem)`.
- `IAtCertificateProvider.Describe(ct)` → `AtCertificateStatus`.
  `TryGetCertificate(out X509Certificate2)` → para o futuro cliente SOAP.

### Infrastructure.At (projeto novo)

- `AccountingAI.Infrastructure.At` — só referencia `Application`.
  `AtOptions` (secção `At`: `Certificate:Directory`, `Certificate:FileName`,
  `Certificate:Path`, `Certificate:Password`, `Environment`).
  `AtCertificateProvider` carrega o PFX com a senha; erros (ficheiro
  inexistente, senha errada, expirado) viram `Problem` em pt-PT, nunca exceção
  não tratada. `AddAccountingAt(configuration)` — liga as opções (com
  `AT_CERTIFICATE_PASSWORD` como alternativa à secção) e regista o *provider*.
- No `.slnx`, na referência da API e no `ModuleDependencyTests`.

### API

- `GET /api/v1/development/at/certificate` — o `AtCertificateStatus`.

### PWA

- Ecrã **«Comunicação com a AT»** (`/comunicacao-at`) — mostra o estado do
  certificado e onde se configura (secção `At`, senha em *user-secrets* /
  `AT_CERTIFICATE_PASSWORD`). O local e a senha não são editáveis pela
  interface.

## Sub-incrementos

### F2-02A — Configuração e estado do certificado

- **Regras:** `Application/At/`; projeto `Infrastructure.At` +
  `AtCertificateProvider` + `AtOptions` + `AddAccountingAt`;
  `GET .../at/certificate`; secção `At` no `appsettings.json` (sem senha); ecrã
  `/comunicacao-at`. `ModuleDependencyTests` atualizado.
- **Validação:** teste do *provider* (PFX auto-assinado gerado no teste →
  metadados; ficheiro inexistente → não configurado; senha errada → `Problem`);
  teste de API (estado `configured:false` sem certificado); teste de interface
  (ecrã + gate de demonstração).

### F2-02B+ — (bloqueado) chamadas SOAP à AT

- Consulta e-Fatura e, depois, submissão de declarações — quando a AT fornecer
  os elementos de teste e confirmar a adesão. Cliente `System.ServiceModel` no
  `Infrastructure.At`, assente no *provider* de F2-02A.

## Decisões em aberto

- **Adesão à AT** — bloqueio externo; F2-02B+ só arranca com os elementos de
  teste.
- **Credenciais por sujeito passivo** (subutilizador + perfil WFA) — por
  empresa, guardadas cifradas; desenho quando F2-02B arrancar.
- **Local do certificado** — `Directory` + `FileName`, com `Path` absoluto a ter
  prioridade.

## Referências

- [`ADR-0008`](../architecture-decisions/ADR-0008-AT-COMMUNICATION.md),
  [`../CERTIFICACAO_AT.md`](../CERTIFICACAO_AT.md).
- F2-05B / `Infrastructure.Ai` — o mesmo padrão de projeto isolado para uma
  dependência externa.
