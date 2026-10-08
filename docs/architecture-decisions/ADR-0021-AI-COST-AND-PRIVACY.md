# ADR-0021 — Custo, limites e privacidade da IA

- Estado: aceite (pedido do utilizador em 2026-10-08)
- Data: 2026-10-08

## Contexto

Uma pergunta simples («qual o lucro do ano?») custou cerca de 0,63 US$. O produtor da
aplicação fornece a chave da IA e paga os consumos, vendendo a IA aos gabinetes e às
empresas por mês; precisa de **medir**, **limitar** e **carregar** o consumo por empresa. E os
dados dos clientes têm de sair o menos possível para fora.

## Porque custava tanto

- O modelo de origem era o mais caro (`claude-opus-5-5`) com raciocínio alargado e até 8 000
  tokens de saída por resposta.
- O enunciado da conversa leva o **catálogo de listagens** (cerca de 9 000 tokens) e repetia-se
  em cada ronda de ferramentas (até 7 por pergunta), sempre a preço normal.

Medido com a API real, a mesma pergunta (com o resultado da ferramenta simulado) passou a custar
cerca de 0,014 US$ — uns 5 vezes menos que o mesmo volume no modelo caro, e cerca de 45 vezes
menos que os 0,63 US$ observados.

## Decisões

1. **Modelo por função** (`AiModels`): o pequeno (`claude-haiku-4-5`) para a conversa, as
   variações, as listagens e a classificação; o intermédio (`claude-sonnet-5-5`) para os relatórios
   de gestão e para a banca. O produtor muda-os na configuração geral. Sem raciocínio alargado na
   conversa; nos relatórios só com modelos que não sejam o pequeno.
2. **Cache do enunciado** na conversa: as rondas seguintes pagam-no a uma fração do preço. O
   consumo registado conta a escrita (1,25×) e a leitura (0,1×) da cache, para nunca ficar abaixo do
   que a Anthropic cobra.
3. **Consumo e limites por empresa** (`AiUsageRecords`, `CompanyAiBudgets`, `AiCreditTopUps`), em
   USD, a moeda em que se paga: cada pedido regista modelo, tokens e custo. A **franquia mensal**
   (por empresa, ou uma por omissão na configuração geral) recomeça todos os meses; o **crédito
   extra** que o produtor junta não caduca e só se gasta depois de a franquia do mês acabar. Sem
   saldo, a IA da empresa recusa (conversa, relatórios, variações, listagens, classificação) até ao
   mês seguinte ou até haver crédito. Sem franquia, só se regista. O pedido que passa o saldo regista-se
   e pode ultrapassá-lo por esse pedido. O produtor vê tudo no ecrã da configuração geral; cada empresa
   vê o seu consumo nas Definições.
4. **Preços:** os de origem são **de referência**. O produtor confirma-os em anthropic.com/pricing e
   corrige-os na configuração geral. Um modelo desconhecido paga-se pelo preço mais alto.
5. **Nomes escondidos** (`AiTextProtector`), ligado por omissão por empresa: o nome da empresa, os
   nomes dos terceiros, os NIF, os IBAN e os emails vão para a IA trocados por marcadores
   (`[TERCEIRO-1]`, `[NIF-2]`…) e voltam repostos na resposta (também nos pedidos de ferramentas,
   para estas encontrarem os nomes). Aplica-se à conversa, aos relatórios de gestão e à
   explicação das variações. **Não** se aplica à classificação de compras, que precisa do nome do
   fornecedor para classificar. É uma redução do que sai, não uma garantia.
6. **Prazo das conversas** por empresa: sem prazo, N dias (apaga-se de 6 em 6 horas e ao mudar o
   prazo), ou «não guardar» (cada pergunta é independente: a resposta volta ao ecrã e nada fica na
   base de dados). O consumo regista-se sempre. Os relatórios de gestão gerados não são conversas e
   mantêm-se.
7. **Âmbito:** o enunciado da conversa obriga a IA a falar só da contabilidade e dos dados da
   empresa, a recusar o resto e a nunca inventar valores; a única fonte de dados são as ferramentas
   de leitura, com os direitos do utilizador.

## O que não se pode fazer

- **Não há** instrução num pedido que impeça a Anthropic de guardar ou treinar com os dados: isso
  decide-se no contrato (termos comerciais, retenção zero, acordo de tratamento de dados).

## Limites

- Os consumos registam-se em USD; a faturação (a empresa, em euros) é do produtor.
- O saldo é verificado antes de cada pedido, não durante: uma conversa a meio de várias rondas pode
  passá-lo.
