# Handoff — próxima sessão Copilot

> **Para a IA que abrir esta sessão:** leia este arquivo inteiro antes de
> qualquer outra coisa. Ele descreve exatamente onde paramos e o que
> precisa ser feito a seguir. Quando o trabalho descrito aqui for
> concluído, **delete este arquivo** no mesmo commit em que rodar a app.

## TL;DR

- Branch atual: **`feat/v2-modernization`** (PR #38 aberta para `master`).
- Toda a modernização foi finalizada e commitada (79/79 tarefas).
- O usuário **só quer rodar localmente** para validar antes de mergear.
- A máquina foi reiniciada por causa de um Docker Desktop travado em
  "Starting Docker Engine..." (o distro WSL `docker-desktop` sumiu — só
  `docker-desktop-data` aparece em `wsl --list --verbose`).
- Próximo passo: subir SQL Server local + rodar a app + smoke manual.

## Pré-condições (verificar primeiro)

1. `git status` — deve estar limpo, na branch `feat/v2-modernization`.
2. `git log --oneline -5` — último commit deve ser
   `6f96a6c fix(security): global anti-forgery enforcement on every mutating endpoint`
   (ou mais recente, se a sessão anterior tiver continuado).
3. `gh pr view 38` — PR deve existir e estar aberta.
4. `docker version` — engine deve responder. Se travar:
   - `Get-Service com.docker.service` → se Stopped, `Start-Service com.docker.service`.
   - `wsl --list --verbose` → tem que aparecer `docker-desktop` *Running*.
     Se não aparecer, abrir Docker Desktop UI → Settings → Troubleshoot →
     **Reset to factory defaults** (perde containers/images locais; nada
     importante neste projeto ainda).
   - Se nada funcionar: pedir ao usuário pra reinstalar o Docker Desktop
     ou usar SQL Server LocalDB / SQL externo.

## Plano de execução (ordem estrita)

### 1. Subir SQL Server 2022 num container

```powershell
docker run -d --name aa-sql -p 1433:1433 `
  -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=YourStrong!Pass1" `
  mcr.microsoft.com/mssql/server:2022-latest

# Confirmar que subiu
docker ps --filter "name=aa-sql"
# Esperar uns 15s para o SQL inicializar
Start-Sleep -Seconds 15
```

Se já existir o container parado (`docker ps -a` mostra `aa-sql`), só
`docker start aa-sql`.

### 2. Configurar user-secrets

```powershell
cd F:\repos\AcademiaAuditiva\AcademiaAuditiva
dotnet user-secrets init  # se ainda não inicializado
dotnet user-secrets set "ConnectionStrings:DefaultConnection" `
  "Server=localhost,1433;Database=AcademiaAuditiva-dev;User Id=sa;Password=YourStrong!Pass1;TrustServerCertificate=True;Encrypt=False"

# Bootstrap admin (qualquer email/senha; será o ADMIN do site)
dotnet user-secrets set "Admin:Email" "lucas.decarli.ca@gmail.com"
dotnet user-secrets set "Admin:InitialPassword" "Admin!LocalDev1"
```

Não setar Facebook/SMTP/AKV — o app degrada graciosamente quando estão
ausentes (foi um dos fixes desta branch).

### 3. Aplicar migrations

O `Program.cs` chama `Database.Migrate()` no startup quando o env **não**
é `Testing`, então normalmente não precisa rodar nada. Se quiser
manualmente (e tiver `dotnet-ef` instalado):

```powershell
dotnet ef database update --project AcademiaAuditiva
```

> **Atenção ARM64:** o `dotnet ef` pode quebrar nesta máquina por falta de
> runtime net8 (só tem 9/10). Se isso acontecer, **deixa o startup migrar
> sozinho** — funciona via roll-forward que o Program.cs já tem implícito
> ao rodar via `dotnet run`.

### 4. Rodar a app

```powershell
cd F:\repos\AcademiaAuditiva
$env:DOTNET_ROLL_FORWARD = "LatestMajor"  # necessário em ARM64 sem runtime 8
dotnet run --project AcademiaAuditiva
```

A URL aparece no console (geralmente `http://localhost:5000` /
`https://localhost:5001`). Os logs Serilog vão pro stdout.

### 5. Smoke manual (validação que vale a pena)

Use o **Edge/Chrome em janela anônima** para evitar cookies presos:

1. **Home anônima** (`/`) renderiza.
2. **Registrar novo usuário** (`/Identity/Account/Register`) — cria conta
   com email qualquer + senha. Confirma se loga automaticamente; o usuário
   já deve receber o role `Student` (verificável via página de exercício
   acessível).
3. **Login com admin** — sair, entrar com `Admin:Email`/`Admin:InitialPassword`
   das user-secrets.
4. **Admin → Users** (`/Admin/Users`) — promover o usuário recém-criado a
   `Teacher`.
5. **Logar como Teacher** → criar uma classroom em `/Teacher/Classrooms`,
   convidar um e-mail novo, criar uma rotina, atribuir.
6. **Validar dashboard** (`/Teacher/Dashboard/Classroom/{id}`) — confirmar
   que **não** mostra contagens malucas (o fix `fix(dashboards):` deve
   estar surtindo efeito; agregados zerados num cenário novo).
7. **Logar como Student** → praticar um exercício
   (`/Exercise/Index?exerciseName=GuessNote` ou similar).
   - DevTools → Network → o POST para `/Exercise/ValidateExercise` **deve**
     ter um header `RequestVerificationToken` (validação do fix CSRF).
   - Após errar e acertar algumas, conferir que `Score`,
     `ScoreSnapshot` e `ScoreAggregate` foram populados (via SSMS / query
     direto: `SELECT TOP 5 * FROM ScoreSnapshots ORDER BY Id DESC`).
8. **`/MyTraining`** — progresso da rotina deve refletir a prática.
9. **Healthchecks**: `/health/live` → 200; `/health/ready` → 200 com
   `Healthy` em todos os checks (DB principalmente).

### 6. Se algo falhar

Categorize:

- **Erro de runtime / 500** → ler stack no console Serilog. Provavelmente
  algo que o rubber-duck previu mas não foi corrigido. Reporta ao
  usuário antes de qualquer fix.
- **Página em branco / redirect loop** → checar se
  `app.UseAuthentication()` continua antes de `UseAuthorization()` em
  `Program.cs` (linha ~227). Foi um dos blockers desta branch.
- **CSRF rejeita POST** → DevTools deve mostrar o header sendo enviado.
  Se não, o shim em `_Layout.cshtml` quebrou.
- **Dashboards com contagens estranhas** → confirmar que o controller
  está lendo de `ScoreAggregates`/`ScoreSnapshots`, **não** de `Scores`.

### 7. Após validação bem-sucedida

1. Apagar este `HANDOFF.md` (` Remove-Item HANDOFF.md `).
2. `git add HANDOFF.md && git commit -m "chore: remove local-run handoff doc"`
3. `git push`.
4. Avisar o usuário que pode mergear o PR #38.

## Contexto adicional

- **Stack:** ASP.NET Core 8 MVC, EF Core 8, Identity, SQL Server, Bicep,
  Container Apps, Key Vault.
- **Repo:** lcarli/AcademiaAuditiva, default branch `master`.
- **PR aberta:** https://github.com/lcarli/AcademiaAuditiva/pull/38.
- **Comandos confiáveis nesta máquina:**
  - `dotnet build AcademiaAuditiva/AcademiaAuditiva.csproj -nologo -v minimal` — 0 errors esperado.
  - `$env:DOTNET_ROLL_FORWARD="LatestMajor"; dotnet test --nologo` — 78 unit + 5 integration passando.
- **Não rodar:** `dotnet ef migrations add` neste host (ARM64 sem runtime 8 padrão; já houve drama).
- **Convenção de commit:** Conventional Commits + sempre incluir trailer
  `Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>`.
- **Identidade git pra commits desta automação:**
  `git -c user.name="lcarli" -c user.email="lcarli@users.noreply.github.com" commit ...`

## Tarefas SQL

Todos os 79 todos da sessão anterior estão `done`. A próxima sessão pode
adicionar novos todos (ex.: `local-smoke-test`) se julgar útil, mas não é
obrigatório — este HANDOFF.md é o contrato.
