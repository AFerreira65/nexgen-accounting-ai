# F1-77 — Exercício ao criar a empresa a partir do SAF-T

## Objetivo

F1-76 passou a marcar o exercício do ficheiro ao importar contas ou
movimentos, mas só para uma empresa que já existia. Quando o NIF do SAF-T
não corresponde a nenhuma empresa e se cria a empresa ali mesmo, o
exercício continuava por definir até se correr um dos passos de importação
— e até lá o topo mostrava «Exercício atual» e os atalhos de período
caíam no ano do sistema. Este incremento fecha essa lacuna.

## Regras

- `POST .../saft/companies` (criação da empresa a partir do SAF-T) aceita
  um campo `fiscalYear` opcional; a PWA envia o exercício do cabeçalho do
  ficheiro. Depois de criar a empresa e associar o utilizador, o exercício
  fica logo definido como o exercício desse utilizador na empresa nova
  (`EnsureDefaultFiscalYearAsync`, o mesmo dos três endpoints de
  importação).
- `EnsureDefaultFiscalYearAsync` passa a ignorar em silêncio um ano fora
  do intervalo 2000–2200 (um SAF-T real nunca lá chega): é um acerto de
  preferência e não deve fazer falhar a criação nem a importação.
- O painel «Empresa criada» mostra o exercício do ficheiro entre os dados
  provisionados.

Sem alteração de esquema. A fase do `/foundation` mantém-se em F1-71.

## Superfície de validação local

Criar uma empresa a partir de um SAF-T cujo NIF não corresponde a nenhuma:
depois de recarregar, o «Exercício» no topo mostra o ano do ficheiro e os
atalhos de período dos relatórios usam-no, sem ter de correr nenhum passo
de importação primeiro.

## Validação efetuada

- `dotnet build`/`dotnet test` na solução inteira sem avisos nem falhas
  (35 testes de domínio, incluindo os de `SetDefaultFiscalYear` de F1-76);
- `AccountingAI.Web`: lint, build e quarenta e quatro testes de interface;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação contra a base `accountingAI`: criar uma empresa a partir de
  um SAF-T de 2026 com um NIF novo grava logo `DefaultFiscalYear = 2026`
  para o utilizador que a criou; a empresa de teste foi removida a seguir.

## Fora deste incremento

- guardar todos os anos alguma vez importados (não só o mais recente),
  para um seletor de exercício com histórico em vez de um único valor.
