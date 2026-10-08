# ADR-0019 — Email: contas com MFA, envio e receção de documentos

- Estado: aceite (pedido e respostas do utilizador em 2026-10-08)
- Data: 2026-10-08

## Contexto

O email serve três coisas: **enviar** relatórios e avisos (F4E-04), **receber**
documentos dos clientes na caixa do gabinete (F4E-03) e o resumo diário de
prazos. O utilizador pediu uma área na ficha do utilizador para configurar o
email, **com contas que têm MFA**, e decidiu: contas Microsoft 365, Gmail /
Google Workspace e servidor próprio (SMTP/IMAP); uma só caixa de receção para o
gabinete, configurada pela empresa e separada das contas pessoais.

## Decisões

1. **Três tipos de conta** (`MailProvider`):
   - **Servidor próprio** (`Password`): SMTP/IMAP com utilizador e senha. Com
     MFA usa-se uma *senha de aplicação*.
   - **Microsoft 365** e **Google**: OAuth2 com código de autorização. O login e
     o MFA fazem-se na página do fornecedor; a aplicação só recebe e guarda,
     cifrado, um **token de renovação**, e renova o token de acesso a cada
     uso. O SMTP/IMAP autentica-se com XOAUTH2.
2. **A aplicação OAuth é do produtor.** O ID e o segredo da aplicação registada
   no Azure (Entra ID) e na Google Cloud ficam em `MailOAuthApps`, cifrados,
   editados só pela conta do produtor (como a configuração da AT, ADR-0017).
   Sem eles, os botões «Ligar com a Microsoft/Google» aparecem desativados e
   dizem porquê. O endereço de resposta a registar é
   `{origem}/api/v1/mail/oauth/callback` (mostrado no ecrã do produtor).
3. **Uma tabela, dois usos** (`MailAccount`): a conta de um **utilizador**
   (`UserSending`, uma por utilizador, configurada por ele em Definições → A
   minha conta de email; ninguém configura a conta de outro) e a **caixa do
   gabinete** (`FirmInbox`, uma por gabinete, só o dono ou um administrador a
   configura). A senha e o token de renovação usam a cifra portátil `enc:v2:`
   e nunca voltam ao ecrã nem à API.
4. **Segurança do OAuth.** O `state` do pedido é cifrado (Data Protection), expira
   em 15 minutos e só vale para o utilizador da sessão que o pediu; um `state`
   de outra pessoa ou alterado é recusado.
5. **Envio (F4E-04).** O botão «Email» das listagens (junto de CSV, Excel e PDF)
   e «Enviar por email» no relatório para a banca abrem uma janela com
   destinatários, assunto e texto; o servidor gera o anexo (PDF ou Excel)
   e envia em nome do utilizador, com a conta dele. Exige poder exportar na
   empresa (`reports.export`). O **resumo diário** (`DailyDigestJob`) envia aos
   dias úteis, a partir das 8 horas, a quem o ligou, as obrigações em atraso ou a
   vencer em 7 dias das empresas de que é responsável e as suas tarefas;
   não envia quando não há nada.
6. **Receção (F4E-03).** O `InboxPollJob` lê a caixa do gabinete de 5 em 5
   minutos (ou «Ler agora»). Cada email guarda-se uma só vez, pelo identificador da
   mensagem (`InboundEmails`). Só os anexos de documentos (PDF, XML, imagens
   com mais de 15 KB — os logótipos de assinatura ficam de fora) passam
   ao arquivo digital da empresa, que se descobre:
   1. pelo **NIF** de uma empresa do gabinete no assunto, no texto ou no nome dos
      anexos (se for exatamente uma);
   2. senão, porque o **remetente** já enviou documentos arquivados numa só empresa;
   3. senão, o email espera na **Caixa de entrada** (Dados) até alguém escolher
      a empresa — o que exige poder juntar documentos nessa empresa
      (`archive.create`) — ou o pôr de parte. Um email só é marcado como lido
      no servidor depois de tratado.
   Os anexos repetidos (mesmo SHA-256) não entram duas vezes no arquivo.

## Limites conhecidos

- **Nunca foi exercitado contra servidores reais** (Microsoft, Google, SMTP,
  IMAP): não há contas nem aplicações OAuth registadas nesta máquina. Está
  verificado com servidores simulados: os pedidos OAuth (endereços, âmbitos,
  corpo), a guarda cifrada, o envio e a receção com os fakes do SMTP/IMAP.
  O primeiro uso real pode pedir ajustes (âmbitos, portas, pasta).
- **Microsoft 365:** o SMTP autenticado pode estar desligado no tenant; o
  administrador tem de permitir o `SMTP AUTH` para as caixas que enviam. Os
  âmbitos usados são `SMTP.Send` e `IMAP.AccessAsUser.All` (delegados).
- **Google:** o âmbito `https://mail.google.com/` é restrito; para contas de fora
  do Workspace da empresa exige verificação da aplicação pela Google.
- A empresa **não** se descobre pelo conteúdo do PDF (nem pelo código QR da
  fatura); fica como melhoria.
- O resumo diário usa a conta de email do próprio destinatário (envia para si
  mesmo); só chega a quem a configurou e ligou.
