# Adesão aos webservices da AT — guia passo a passo

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
  `asi-cd@at.gov.pt` — a aguardar resposta.
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

A chave privada gera-se nesta máquina e nunca sai dela. Os campos não podem
ter acentos nem cedilhas. Exemplo do manual, a adaptar:

```bash
openssl req -new -subj "/C=PT/ST=<Distrito da sede>/L=<Local da sede>/O=<Designacao legal>/OU=<Departamento>/CN=518104486/emailAddress=<email de contacto>" -newkey rsa:4096 -nodes -out 518104486.csr -keyout 518104486.key
```

- `CN` é o NIF do produtor (9 caracteres).
- **Tamanho da chave:** o manual do e-Fatura ainda diz 2048 bits; o
  [`CERTIFICACAO_AT.md`](CERTIFICACAO_AT.md) regista que desde 2025 a política
  da AT exige 4096. Confirmar na página «Produtores de Software» no momento da
  adesão e usar o valor que lá estiver.
- O `.key` fica fora do repositório (o `.gitignore` já exclui certificados e
  chaves) e com cópia de segurança.
- O certificado emitido vale 12 meses; a renovação repete este passo.

## Passo 4 — Aderir ao serviço (produção)

Depois de os testes passarem, em **Portal das Finanças > e-Fatura >
Produtores de Software > Aderir ao Serviço**: aceitar os termos e colar o
CSR. A AT responde por email com o certificado assinado.

## Passo 5 — Montar o PFX e configurar a aplicação

Juntar o certificado recebido à chave privada:

```bash
openssl pkcs12 -export -in <certificado da AT>.crt -inkey <NIF>.key -out <NIF>.pfx
```

Configurar a secção `At` (local e nome do ficheiro) e a senha do PFX em
*user-secrets* ou `AT_CERTIFICATE_PASSWORD`, como descrito em
[`phase-2/F2-02A-AT-CERTIFICATE-CONFIG.md`](phase-2/F2-02A-AT-CERTIFICATE-CONFIG.md).
O ecrã «Comunicação com a AT» mostra se o certificado foi lido.

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

## O que fica à espera da resposta da AT

- Se um produtor só de contabilidade pode aderir (a dúvida central).
- O certificado de testes e a chave pública — sem eles o cliente SOAP (F3-15)
  só pode ser testado com respostas sintéticas.

## Fontes

- [Manual e-Fatura — aspetos genéricos](https://info.portaldasfinancas.gov.pt/pt/apoio_ao_contribuinte/Outras_entidades/Suporte_tecnologico/Webservice/e_Fatura/Documents/Comunicacao_dos_elementos_dos_documentos_de_faturacao_aspetos_gerais.pdf) (modelo do email, CSR, adesão)
- [Webservices da AT — índice](https://info.portaldasfinancas.gov.pt/pt/apoio_ao_contribuinte/Outras_entidades/Suporte_tecnologico/Webservice/) (IVA, IRC, IES, obrigações acessórias, DMIS)
- [Página de produtores de software](https://faturas.portaldasfinancas.gov.pt/painelInicialProdSoftware.action)
