# F1-02B — Git, CI e gates de engenharia

## Estado

Implementada e validada localmente em 30 de agosto de 2026. O workflow só ficará confirmado remotamente depois de o repositório ser publicado num serviço Git compatível com GitHub Actions.

## Entregas

- repositório Git local no ramo `main`;
- workflow Windows 2025 com actions oficiais fixadas por SHA imutável;
- build e testes .NET, LocalDB, smoke sintético, smoke WinForms, lint, testes e build Web;
- auditoria de vulnerabilidades NuGet e npm;
- Gitleaks descarregado numa versão e checksum fixos;
- Dependabot para NuGet, npm e GitHub Actions;
- exclusão de SAF-T, documentação PHC, bases, backups, certificados, credenciais e artefactos locais.

## Gates locais

```powershell
dotnet restore ".\NexGen Accounting AI.slnx"
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -SyntheticOnly -NoRestore
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-security.ps1
```

Sem `-SyntheticOnly`, o build preserva o smoke local com os SAF-T existentes, que não são publicados no repositório. Se o servidor Web estiver aberto, as dependências instaladas são validadas sem executar `npm ci` sobre ficheiros nativos em uso. O lançador executa a API em Debug para não bloquear o build Release.

## Limites

- o workflow foi validado estruturalmente e os mesmos comandos passaram localmente, mas ainda não correu num GitHub remoto;
- regras de branch protection e revisões obrigatórias dependem da criação do repositório remoto;
- LocalDB no CI é um gate sintético e não substitui SQL Server Express/staging.
