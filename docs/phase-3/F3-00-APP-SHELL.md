# F3-00 — Moldura da aplicação

Pedido do utilizador (2026-10-03): um layout moderno. O menu lateral era uma
lista plana de 26 entradas que obrigava a deslocar; a Fase 3 vai acrescentar
mais ecrãs, por isso a moldura muda antes deles.

## O que mudou

- **Menu em grupos** — Trabalho, Mapas, Fiscal, Gestão, Exercício, Cadastros,
  Dados, Administração — pela ordem em que o contabilista os usa. «Tarefas»
  sobe para o primeiro grupo.
- **Painel flutuante claro**, com o item ativo marcado por uma pastilha e uma
  barra de destaque; tema escuro pelas mesmas variáveis (`--nav-*` em
  `.product-shell`).
- **Recolhível para uma régua de ícones** (botão no topo do menu; a escolha
  fica no navegador em `accounting-ai.sidebar-collapsed`). Recolhido, cada
  ligação mantém o nome acessível e mostra-o como dica.
- **Gaveta em ecrãs até 850 px** — o menu sai do fluxo e abre pelo botão novo
  da barra de topo, com fundo escurecido; fecha ao escolher um ecrã.
- **Barra de topo sem faixa** — funde-se com o fundo; a empresa e a pesquisa
  passam a ser cartões.

Só mudam `app/AppLayout.tsx` e o bloco da moldura em `styles.css`; os ecrãs não
foram tocados.

## Validação efetuada

- `App.test.tsx`: os grupos, a ligação «Terceiros» e o recolher/expandir; os
  restantes testes de navegação continuam verdes.
- Visto no navegador em 1440 px (aberto e recolhido) e em 375 px (gaveta), em
  modo de demonstração (`web-demo` em `.claude/launch.json`, que serve a PWA
  com os dados de demonstração e sem API).
- Tema escuro: visto na moldura (menu e barra de topo). As tabelas e alguns
  campos dos ecrãs já não acompanhavam o tema escuro antes desta alteração e
  continuam por tratar.
- **Por ver:** os ecrãs ligados à base real — precisam de uma sessão iniciada
  pelo utilizador.
