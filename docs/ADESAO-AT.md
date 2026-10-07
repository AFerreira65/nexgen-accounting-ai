# Adesão aos webservices da AT — guia passo a passo

> **Atualização de 2026-10-07 — a AT respondeu.** A resposta de `asi-cd@at.gov.pt`
> trouxe, por ligação temporária, o certificado de testes e a chave pública
> (`ChaveCifraPublicaAT2027.cer`) e a **senha do PFX** (indicada na própria
> resposta; não fica registada neste repositório). Os dois ficheiros foram
> conferidos — o PFX abre com a senha, com validade de 10/07/2026 a 06/01/2027;
> a chave pública vale até 15/04/2027 — e ficam em
> `%LOCALAPPDATA%\AccountingAI\at-testes\`, fora do repositório. Desde o
> mesmo dia o certificado, a chave pública e a senha (**cifrada**) carregam-se
> na aplicação pelo **utilizador produtor** (ecrã «Configuração geral», que só
> ele vê) e ficam **na base de dados**, para qualquer posto os usar sem
> instalar nada →
> [ADR-0017](architecture-decisions/ADR-0017-PRODUCER-USER-AND-PORTABLE-AT-SETTINGS.md);
> a secção `At` dos *user-secrets* fica como alternativa. A AT lembra que estes
> certificados **não dispensam a certificação do software**. Passo seguinte:
> os subutilizadores com a permissão de cada serviço e «Validar na AT».

> **Atualização de 2026-10-05.** O certificado de testes (`TesteWebservices.pfx`,
> válido até 06/01/2027) e a chave pública da AT (válida até 15/04/2027) estão
> em descarregamento público na página «Certificados de segurança» do Suporte
> tecnológico do Portal das Finanças, **mas protegidos por senha**, que não
> está publicada. A senha vem na resposta da AT ao email, ou na área de
> produtores de software do e-Fatura (com login). Os testes continuam, por
> isso, a depender disso. O que já está feito e o que falta configurar está em
> [`phase-4/README.md`](phase-4/README.md), «Fase 4A».

O Bloco C da [Fase 3](phase-3/README.md) (consulta de faturas, validação e
submissão de declarações) depende de a AT reconhecer o NexGen Accounting AI como
software de um produtor e emitir os certificados. Este guia junta o que os
manuais da AT pedem e o que só o titular pode fazer. O enquadramento geral
está em [`CERTIFICACAO_AT.md`](CERTIFICACAO_AT.md).

Quem faz o quê: os passos 1, 2, 4 e 6 são feitos pelo titular no Portal das
Finanças e por email; o passo 3 (gerar a chave) faz-se nesta máquina; o resto
é desenvolvimento.

## Estado (2026-10-03)

- Produtor: RumaoAlgarismo - Unipessoal, Lda., NIF 518104486.
- Pedido do certificado de testes e da chave pública **enviado** por email a
  `asi-cd@at.gov.pt` (2026-10-03) — **respondido a 2026-10-07**, ver a atualização no topo.
- Subutilizador de testes criado: `518104486/2`.
- O formulário «Aderir ao serviço» pede só NIF, email, CSR e a aceitação do
  contrato — não pede número de certificado de programa de faturação. A
  adesão (produção) fica para depois dos testes.
- A página «Testar Webservice» lista os serviços de IRC, IVA, IES, Imposto do
  Selo, obrigações acessórias e recapitulativa; os WSDL e manuais estão em
  [`at-wsdl/`](at-wsdl/README.md).

## Passo 1 — Decidir quem é o produtor

O produtor é a entidade (NIF) que assina o pedido e fica titular do
certificado. Decidido em 2026-10-03: **RumaoAlgarismo - Unipessoal, Lda.,
NIF 518104486**. Tudo o que se segue usa esse NIF.

## Passo 2 — Pedir os elementos de testes (email)

O manual do e-Fatura manda pedir por email a `asi-cd@at.gov.pt` a chave
pública do sistema de autenticação e o certificado SSL de testes. O modelo
oficial pressupõe um programa de faturação com número de certificado; o
NexGen Accounting AI é um programa de contabilidade e não tem, por isso o pedido
deve dizê-lo e pedir a confirmação de elegibilidade. Rascunho:

> **Para:** asi-cd@at.gov.pt
>
> **Assunto:** Obtenção do certificado SSL para testes e chave pública do
> sistema de Autenticação – NIF 518104486
>
> Exmos. Senhores,
>
> O Produtor de Software RumaoAlgarismo - Unipessoal, Lda. (NIF 518104486) vem por este meio
> solicitar o envio dos seguintes elementos para desenvolvimento e testes de
> comunicação por webservice:
>
> - Chave pública do Sistema de Autenticação do Portal das Finanças;
> - Certificado SSL para comunicação com os endereços de testes dos
>   webservices.
>
> Estes elementos destinam-se ao programa **NexGen Accounting AI**, um programa de
> contabilidade para contabilistas certificados e sujeitos passivos. O
> programa não emite faturas nem outros documentos fiscalmente relevantes,
> pelo que não é um programa de faturação e não tem número de certificado
> atribuído ao abrigo da Portaria n.º 363/2010.
>
> Pretendemos integrar os seguintes serviços, nos termos dos respetivos
> manuais de integração:
>
> - Declaração Periódica de IVA e Declaração Recapitulativa;
> - Declaração Modelo 22 de IRC;
> - IES / Declaração Anual;
> - Obrigações acessórias (DMR, Modelos 10, 30 e 39) e DMIS;
> - Consulta de faturas do e-Fatura na qualidade de adquirente e de emitente
>   (serviço `fatshareFaturas`).
>
> Solicitamos ainda a confirmação de que um produtor de software de
> contabilidade, sem programa de faturação certificado, pode aderir a estes
> serviços, em particular ao de consulta de faturas, e de quais os passos
> adicionais caso haja algum.
>
> Aguardamos a vossa resposta.
>
> Com os melhores cumprimentos,
> António Ferreira — RumaoAlgarismo - Unipessoal, Lda.

## Passo 3 — Gerar a chave e o pedido de certificado (CSR)

A chave privada gera-se nesta máquina e nunca sai dela. Faz-se com o script
`scripts\new-at-csr.ps1`, que valida os campos como a AT exige (sem acentos
nem cedilhas, dentro dos tamanhos máximos, `CN` = NIF do produtor), guarda a
chave fora do repositório, numa pasta só do utilizador
(`%LOCALAPPDATA%\AccountingAI\at-producao`), e **recusa substituir uma chave
que já exista**:

```powershell
.\scripts\new-at-csr.ps1 -State "<Distrito da sede>" -Locality "<Local da sede>" `
    -Organisation "<Designacao legal>" -Department "<Departamento>" -Email "<email de contacto>"
```

- **Tamanho da chave: 2048 bits.** É o que **todos** os manuais da AT indicam
  (e-Fatura de outubro de 2025, IVA, IRC, IES, recapitulativa e DMIS). A
  indicação de 4096 que consta de [`CERTIFICACAO_AT.md`](CERTIFICACAO_AT.md)
  não se confirma em nenhum manual: se o formulário «Aderir ao Serviço» pedir
  outro tamanho, usa-se `-Bits` com o valor do formulário.
- Tamanhos máximos: `ST` 32, `L` 32, `O` 180, `OU` 180, `E` 80.
- O ficheiro a colar no formulário é o `518104486.csr` que o script mostra no
  fim (o conteúdo é confirmado no próprio ecrã). **O `.key` não se envia.**
- O `.key` tem cópia de segurança e o `.gitignore` já exclui chaves e PFX.
- O certificado emitido vale 12 meses; a renovação repete este passo (com uma
  chave nova).

## Passo 4 — Aderir ao serviço (produção)

Depois de os testes passarem, em **Portal das Finanças > e-Fatura >
Produtores de Software > Aderir ao Serviço**: aceitar os termos e colar o
CSR. A AT responde por email com o certificado assinado.

## Passo 5 — Montar o PFX e configurar a aplicação

Quando a AT devolver o certificado assinado, `scripts\assemble-at-pfx.ps1`
confere que **corresponde à chave** guardada, que o `CN` é o NIF do produtor
e a validade, pede a senha do PFX (não aparece no ecrã nem no histórico) e cria
o PFX:

```powershell
.\scripts\assemble-at-pfx.ps1 -Certificate C:\Downloads\518104486.crt
```

A senha escolhida é a que depois se escreve na aplicação; não fica registada
em lado nenhum, por isso guarde-a num sítio seguro. Carregar o PFX, a senha e a
chave pública da AT no ecrã «Configuração geral» da conta do produtor (ver
[ADR-0017](architecture-decisions/ADR-0017-PRODUCER-USER-AND-PORTABLE-AT-SETTINGS.md));
em alternativa, a secção `At` e a senha em *user-secrets* ou
`AT_CERTIFICATE_PASSWORD` ([`phase-2/F2-02A-AT-CERTIFICATE-CONFIG.md`](phase-2/F2-02A-AT-CERTIFICATE-CONFIG.md)).
O ecrã «Comunicação com a AT» mostra se o certificado foi lido. O certificado
de produção só vale nos endereços de produção (portas 4xx); o de testes só nos
de testes (7xx).

## Passo 6 — Subutilizadores no Portal das Finanças

Em **Autenticação de Contribuintes > Gestão de Utilizadores**, criar um
subutilizador com o perfil de cada serviço. A senha principal nunca entra na
aplicação.

| Serviço | Perfil do contabilista | Perfil do contribuinte |
|---|---|---|
| Declaração periódica de IVA | NIF do contabilista | WVA |
| Declaração recapitulativa | WDR | WDR |
| Modelo 22 | TRC | IRC |
| IES | TIE | IES |
| Obrigações acessórias | TOA | perfil da aplicação respetiva |
| e-Fatura (consulta de faturas) | — | WFA, em cada cliente |

Com plenos poderes declarativos, o contabilista certificado submete só com a
sua senha e o NIF do cliente. A consulta de faturas é a exceção: precisa do
subutilizador WFA de cada cliente — é o que o painel «Configuração da AT»
(F2-02B) já guarda cifrado.

## Estado do código antes de pedir a adesão (2026-10-07)

| Serviço da AT | Cliente na aplicação | Endereços (testes / produção) | Estado |
|---|---|---|---|
| e-Fatura, consulta de faturas (adquirente) | `EFaturaAtGateway` | 725 / 425 | Feito (F4A-03). Em testes a AT responde, mas sem documentos |
| Declaração periódica de IVA | `VatReturnAtGateway` | 706 / 406 | Feito, por testar com dados |
| Modelo 22 de IRC | `Modelo22AtGateway` | 705 / 405 | Feito, por testar com dados |
| IES / Declaração Anual (contabilista certificado) | `IesAtGateway` | 707 / 407 | Feito, por testar com dados |
| Declaração recapitulativa | — | 726 / 426 | **Falta o cliente** (os dados já se apuram, F3-09A) |
| Obrigações acessórias (DMR, Modelos 10, 30, 39) | — | 718 / 418 | **Falta** |
| DMIS | — | 721 / 421 | **Falta** |
| Registo de faturas (`fatcorews`) | — | 723 / 423 | Fora do âmbito: a aplicação não emite faturas |

Os endereços dos quatro clientes feitos foram conferidos com os manuais da AT.
Do lado do produtor está feito: a conta de produtor, o carregamento do
certificado, da senha e da chave pública na base de dados, a verificação do
estado do certificado, e os scripts do CSR e do PFX (passos 3 e 5).

Fica em aberto, e só se resolve com a AT ou no formulário:

1. **Se uma só adesão serve todos os serviços.** Todos os manuais mandam aderir
   em «e-Fatura > Produtores de Software > Aderir ao Serviço», mas só o
   formulário (com login) mostra se há uma adesão por serviço.
2. **Elegibilidade sem programa de faturação certificado.** A AT forneceu os
   elementos de testes sem objeções, mas a elegibilidade para produção não
   está confirmada por escrito (ver [`CERTIFICACAO_AT.md`](CERTIFICACAO_AT.md)).
3. **O tamanho da chave** que o formulário pede (os manuais dizem 2048 bits).

## O que fica à espera da resposta da AT

- Se um produtor só de contabilidade pode aderir (a dúvida central).
- O certificado de testes e a chave pública — sem eles o cliente SOAP (F3-15)
  só pode ser testado com respostas sintéticas.

## Fontes

- [Manual e-Fatura — aspetos genéricos](https://info.portaldasfinancas.gov.pt/pt/apoio_ao_contribuinte/Outras_entidades/Suporte_tecnologico/Webservice/e_Fatura/Documents/Comunicacao_dos_elementos_dos_documentos_de_faturacao_aspetos_gerais.pdf) (modelo do email, CSR, adesão)
- [Webservices da AT — índice](https://info.portaldasfinancas.gov.pt/pt/apoio_ao_contribuinte/Outras_entidades/Suporte_tecnologico/Webservice/) (IVA, IRC, IES, obrigações acessórias, DMIS)
- [Página de produtores de software](https://faturas.portaldasfinancas.gov.pt/painelInicialProdSoftware.action)
