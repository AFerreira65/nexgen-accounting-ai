# ADR-0020 — Portal do cliente

- Estado: aceite (decisões do utilizador em 2026-10-08)
- Data: 2026-10-08

## Contexto

O utilizador pediu um portal para os clientes do gabinete: o cliente recebe
**um link por email** e, ao **confirmar a senha, a conta fica ativa**; vê
**documentos, relatórios, balancetes e outros relatórios de gestão**. Os emails
saem da caixa do gabinete (ADR-0019), cujas credenciais se introduzem na
aplicação.

## Decisões

1. **Conta à parte.** `ClientPortalAccount` não é um `UserAccount`: não tem
   empresas do gabinete nem permissões. Uma conta tem acesso a uma ou mais
   empresas (`ClientPortalAccess`), a mesma conta serve vários convites.
2. **Convite por email.** O gabinete convida (Dados → Portal do cliente). A
   aplicação cria um token aleatório de 256 bits, guarda só o seu SHA-256, e envia
   `…/cliente/convite?token=…` com a conta de email do gabinete
   (`MailAccountService.SendFromFirmAsync`). O link vale **7 dias** e só se
   usa uma vez. Sem caixa de correio configurada, ou se o envio falhar, o convite
   fica criado e o ecrã mostra o link para o gabinete o enviar à mão.
3. **A senha ativa a conta.** O cliente escolhe uma senha (pelo menos 10
   caracteres, com letras e números) e a conta passa de «Convidado» a
   «Ativo». Guarda-se só o hash (`PasswordHasher`). «Nova senha» no ecrã do
   gabinete envia um link novo a uma conta já ativa. Cinco senhas erradas
   bloqueiam a conta durante 15 minutos.
4. **Sessão própria.** O portal usa um segundo esquema de autenticação
   (`Portal`, cookie `NexGenAccountingAI.Portal`, `SameSite=Strict`, 8 horas). A política
   por omissão da API só aceita o cookie da aplicação, por isso uma sessão do
   portal **nunca** chega a um endpoint do gabinete, e uma sessão do gabinete não
   vale no portal. A conta verifica-se na base de dados a cada pedido: tirar o acesso
   corta logo a sessão em curso.
5. **O cliente só vê o que o gabinete publicou** (`PortalItem`), por tipo:
   Documentos, Relatórios, Balancetes e Gestão. Publica-se:
   - uma **fotografia** em PDF ou Excel do que um ecrã mostra (botão «Portal»
     junto de CSV/Excel/PDF/Email) ou do relatório para a banca («Publicar no
     portal»); se os dados mudarem, publica-se de novo;
   - um **documento do arquivo digital** («Partilhar no portal»), que se lê do
     arquivo em vez de se copiar.
   O gabinete retira um ficheiro quando quiser.
6. **Permissões do gabinete** (ADR-0018): `portal.view` (ver clientes e o que
   está publicado) e `portal.publish` (convidar, tirar o acesso, publicar,
   retirar). Não incluídas nos grupos de origem «Consulta» e «Lançamentos» além
   do `view`.
7. O portal serve-se na mesma aplicação, em `/cliente`, sem o menu nem o
   contexto de trabalho do gabinete.

## Complementos (2026-10-08)

- **O cliente envia documentos.** No portal, «Enviar documentos ao gabinete»: PDF, imagens e XML, até 15 MB,
  no máximo 50 por dia por conta. Cada ficheiro vai para o arquivo digital da empresa (sem lançamento, com uma
  nota de quem o enviou) e fica registado em `ClientPortalUploads`, para o cliente ver «Enviados por si» e o
  gabinete ver quem enviou o quê (Portal do cliente → «Enviados pelos clientes»). Um ficheiro repetido é recusado.
- **«Esqueci-me da senha».** O cliente pede um link novo em `/cliente`; a resposta é sempre a mesma, exista ou
  não a conta, só se atende um pedido de 5 em 5 minutos e a senha antiga vale até se escolher a nova.

## Limites

- O cliente só consulta, descarrega e envia; não vê o que outros clientes enviaram.
- Uma publicação é uma cópia do momento: não se atualiza sozinha.
- O link do convite usa o endereço por onde o gabinete acedeu à aplicação
  (`https://servidor/cliente/convite…`); para clientes de fora, a aplicação tem de
  estar publicada num endereço que eles alcancem.
- A senha nova também se pode pedir ao gabinete («Nova senha» no ecrã do portal).
