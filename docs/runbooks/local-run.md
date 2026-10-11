# Local run: the whole stack on your machine

Status: **written, not run in the authoring environment.** The container engine (Docker) was not running there, so no step below that starts a container was executed. Each step is taken from the code and configuration named next to it; what was and was not run is in the table at the end. Nothing here deploys anything (AGENTS.md, PROJECT-BRIEF §2).

Languages: **English** first, then **Polski** (below). The two halves have the same numbered sections.

Related: [`scripts/README.md`](../../scripts/README.md) (the setup and smoke scripts, the MCP path, the troubleshooting table of the AppHost), [ADR-0079](../adr/0079-local-stack-pgadmin-interview-config-dev-credit-grant.md) (why the stack looks like this), [ADR-0003](../adr/0003-identity-authservice-as-pinned-image.md) (authservice as a pinned image), [web app deploy runbook](web-app-deploy.md) (not for local use).

---

## English

### 1. What you need

| Tool | Version | Why |
|---|---|---|
| .NET SDK | the one in [`global.json`](../../global.json) (10.x, roll-forward within the feature band) | builds and runs the AppHost and the services |
| Docker Desktop, or Podman | a running daemon | Postgres, pgAdmin and authservice run as containers |
| Node.js | 22 or newer | the web app |
| pnpm | the version the web app uses ([`web/app/package.json`](../../web/app/package.json)); `corepack enable` installs it | the web app's package manager |
| git | any recent | clone, and the pre-commit secret-scan hook |

Windows: run the commands in PowerShell. Paths below use `/`; PowerShell accepts both.

### 2. Set up once: `scripts/setup.sh` or `scripts/setup.ps1`

Run `scripts/setup.sh --check` (or `scripts/setup.ps1 -Check`) first. It only reports what is missing, and changes nothing.

Then run `scripts/setup.sh` (or `scripts/setup.ps1`). What it does, step by step, as the code says:

1. **Prerequisites**: git, dotnet, node, pnpm, a running Docker daemon. `setup.sh` also checks that `dotnet --version` starts with `10.`. It stops if something is missing.
2. **Git hook**: sets `core.hooksPath` to `scripts/hooks`, so every commit runs the secret scan.
3. **Local secret store** (`dotnet user-secrets`, never a file in the tree):
   - creates a **dev-only** RSA-2048 signing key for authservice (`Parameters:authservice-jwt-private-key`). `setup.sh` uses `openssl`; `setup.ps1` uses the .NET runtime. If the key is missing, the AppHost makes an ephemeral one per run.
   - creates a dev-only MCP client secret and a dev-only token-encryption key for authservice. They are used only when the MCP path is configured (see `scripts/README.md`).
4. **Optional integrations**: the text printed here says no optional integration is wired. That is out of date: the model and payment settings in section 7 and 8 are read from user-secrets by the AppHost, not by the setup script. The script changes nothing for them.

Re-running the script is safe: existing secrets are kept.

### 3. Start the stack

```bash
dotnet run --project src/ExitInterviewAgent.AppHost
```

The first run pulls images (Postgres 17, pgAdmin, the pinned authservice image) and builds the services, so it takes a while. The console then prints the **Aspire dashboard** address, which includes a login token (a `?t=` parameter). Open that line: it is the only place that lists every resource, its state, its logs and its endpoints. The port of the dashboard is printed by Aspire; it is not fixed in this repository.

What starts, and where it listens on your machine:

| Resource | Address on your machine | Fixed? | Notes |
|---|---|---|---|
| web (Next.js + BFF) | http://localhost:3000 | yes (3000) | the checkout returns the payer here, so the port cannot float |
| authservice (`ghcr.io/konradcinkusz/authservice:v0.3.4`) | http://localhost:5100 | yes (5100) | the JWKS and issuer address that interview-service and the web app use |
| pgAdmin | http://localhost:5050 | yes (5050) | this machine only (section 4) |
| interview-service | the port shown in the dashboard | no (Aspire assigns it) | `/health` lists the integrations; the smoke script needs this port |
| postgres 17 | the port shown in the dashboard | no | one server, two databases: `interviewdb` (interview-service) and `authdb` (authservice); data in the Docker volume `exit-interview-agent-pgdata` |

The AppHost prints the same list in its startup banner, and `GET <interview-service>/health` lists each integration with `configured` true or false and a reason.

### 4. pgAdmin: browse the database

1. Open http://localhost:5050.
2. **Login email**: `admin@domain.com`. This is the default that the Aspire PostgreSQL package (13.6.0) sets for pgAdmin (`PGADMIN_DEFAULT_EMAIL`). If you have changed it, use what you set.
3. **Password**: the AppHost parameter `pgadmin-password`. Set your own so you know it:

   ```bash
   dotnet user-secrets set "Parameters:pgadmin-password" "<a password you choose>" --project src/ExitInterviewAgent.AppHost
   ```

   Without it the AppHost generates a password for each run; it is then not kept anywhere you can read later, so set your own. Restart the stack after changing it.
4. **Server**: the Aspire package writes the postgres server into pgAdmin automatically (its `WritePgAdminServerJson` step). Expand the server in the left tree and open the `interviewdb` and `authdb` databases.
5. **Database password**: if pgAdmin asks for the postgres password, take it from the **postgres** resource in the dashboard, which shows its connection string. The password is generated for each run.

Only `interviewdb` holds interview data (sessions are in memory, see section 6); the ledger, credits and purchases are in `interviewdb`, and the accounts are in `authdb`.

### 5. authservice: the image, and getting it

The AppHost runs `ghcr.io/konradcinkusz/authservice:v0.3.4`, a pinned tag; never `latest` (ADR-0003). It runs its own migrations (`Database__SchemaMode=Migrate`) and its own database `authdb`.

**Pulling the image.** If the pull is refused (`pull access denied` or `unauthorized`), the package is private for you. Log in with a GitHub personal access token that has `read:packages`:

```bash
echo "<your token>" | docker login ghcr.io -u <your GitHub user> --password-stdin
```

Not verified here: this environment cannot pull the image (ADR-0003 records 403 answers from the sandbox proxy), so whether the pull needs a login is an open fact until someone runs it.

**No image, or no pull: run without identity.** Start with `Identity:Enabled=false` (the AppHost reads it from configuration, so the environment variable `Identity__Enabled=false` works):

```bash
Identity__Enabled=false dotnet run --project src/ExitInterviewAgent.AppHost
```

Then the protected endpoints answer 401, `/health` says `identity: not configured`, and **the web interview cannot run** (every `/api/v1` call is a 401). Use this only to check the rest of the stack.

**Email confirmation is not needed locally.** The AppHost sends no SendGrid key. In authservice `v0.3.4`, email confirmation is required only when a SendGrid key is set (`requireConfirmedEmail = canSendEmail` in `Program.cs`), and a new account is confirmed at once when it is not (`EmailConfirmed = !requireVerification` in the register endpoint). So you can register and sign in without a mail. The `email_verified` claim is **not** in `v0.3.4` (the claim is on authservice's main branch, PR #73, and needs a new image release by its owner). Because of that, the AppHost sets `Interviews__RequireVerifiedEmail=false` (ADR-0079).

### 6. The mock interview, step by step (no key, no money)

The stack starts with the **scripted mock** model: no network call, no cost. Its wording is scripted, not generated, so judge the flow here, not the quality (section 7 is for that).

1. **Check the stack.** `GET <interview-service>/health` shows `interview-model` as not configured, with the mock named in the detail. That is expected.
2. **Create an account.** Open http://localhost:3000/register. Accept the terms and the privacy notice (both are required), then **Create account**. Sign-up does not send an e-mail locally; the account is confirmed at once (section 5).
3. **Sign in.** http://localhost:3000/login, then **Sign in**.
4. **Accept the current terms** on the consent page (`/consent`) when the app asks. The app will not show the interview until you do.
5. **Start.** Open http://localhost:3000/interview. Tick the box that says you have read the conditions and agree to talk with an AI interviewer, then **Start the interview** (Polish: *Rozpocznij rozmowę*). The interviewer says at the start that it is an AI.
6. **Talk.** Type in the box and press **Send** (Enter sends, Shift+Enter starts a new line). Answer a few questions. Do not use real names of people or of your employer; the interview is for checking the flow.
7. **Result.** When the interview ends, the page shows **Your record and draft texts**, the record and the draft texts (tiles), a copy button for each text, and a download of `tiles.html`. The result can take a moment: the page shows a loading message until the tiles are ready.
8. **Delete.** Use **Delete everything now** on the result page (confirm with **Yes, delete it** if asked), or **Stop and delete** during the interview. The app then shows that nothing from the interview is kept. Deleting is always available (the consent text says so).
9. **Start another** with **Start another interview**, or sign out and back in.

Rules you will meet, as the code states them: one open interview per account (a second start answers 409 `interview_in_progress`); a finished interview stays readable for 30 minutes, then it is wiped; an idle interview expires after 30 minutes.

### 7. A real model (Anthropic API key, paid by you)

**Costs money.** Each interview is billed to the account of the key you give. Set a spend limit in the provider's console before you use a real key, and use a key that is only for this project.

Put the values in user-secrets. Never paste them into a file, a commit or a chat:

```bash
dotnet user-secrets set "Parameters:anthropic-api-key" "<your key>" --project src/ExitInterviewAgent.AppHost
dotnet user-secrets set "Parameters:interviews-model" "<a model id from your provider's console>" --project src/ExitInterviewAgent.AppHost
# optional: the workspace id the provider's console shows (not a secret)
dotnet user-secrets set "Parameters:anthropic-workspace-id" "<workspace id>" --project src/ExitInterviewAgent.AppHost
```

PowerShell takes the same commands. Keep the quotes; the path can use `\` or `/`.

What the settings do, from the AppHost:

- `anthropic-api-key` (secret): with a key set, the provider defaults to the Anthropic provider and the key is passed to interview-service as `ANTHROPIC_API_KEY`. Without it, the provider defaults to the mock.
- `interviews-model`: **there is no default for a real provider.** Set it to a model id from your provider's console. The repository names no model; a missing or wrong id makes `interview-model` DEGRADED with the reason, and interviews answer 503.
- `interviews-provider`: set it to force a provider (`anthropic` or `mock`).

Restart the stack after changing user-secrets (the AppHost reads them at start). Then:

- `GET <interview-service>/health`: `interview-model` configured, with `provider 'anthropic' is configured; the key is never reported`. The key itself is never shown.
- Run the interview from section 6. Read the answers yourself and judge them. The mock's result says nothing about quality, and no real-model quality result exists in this repository yet (open item W9 in [the web plan](../architecture/web-app-plan.md)).

To go back to the mock: remove the key with `dotnet user-secrets remove "Parameters:anthropic-api-key" --project src/ExitInterviewAgent.AppHost` and restart.

### 8. The payment path, locally (no money)

By default starts are free (`interviews-require-credit` is `false`). To practise the credit gate:

1. Turn the gate on and restart:

   ```bash
   dotnet user-secrets set "Parameters:interviews-require-credit" "true" --project src/ExitInterviewAgent.AppHost
   ```

2. Start an interview with no credit: the API answers **402 `payment_required`**, and the web page offers **Buy one interview**.
3. **Buy one interview does not add a credit locally.** The development payment provider returns you to the success address at once (with `?checkout=fake`) and sends no webhook. A credit comes only from a signed payment event, which the local stack does not send. Use the development grant instead.
4. **Grant credits (development only)**: `POST /api/v1/dev/grant-credits` with `{"quantity": 1}` (1 to 10). It needs an account token (the browser keeps its token in an HTTP-only cookie, so get one by signing in through the API, below). The route exists only when the environment is Development, the payment provider is the fake one and checkout is configured; the AppHost sets all three. Anywhere else it answers 404. The grant is recorded as `dev-grant:` in the ledger, so it is visibly not a provider event.

Get a token (replace the e-mail and password with your account's; this is the login endpoint of authservice):

```bash
TOKEN=$(curl -sS -X POST http://localhost:5100/api/v1/auth/login -H 'content-type: application/json' \
  -d '{"email":"<your e-mail>","password":"<your password>"}' | python3 -c 'import json,sys; print(json.load(sys.stdin)["accessToken"])')
curl -sS -X POST http://localhost:<interview port>/api/v1/dev/grant-credits -H "authorization: Bearer $TOKEN" \
  -H 'content-type: application/json' -d '{"quantity":1}'
curl -sS http://localhost:<interview port>/api/v1/credits -H "authorization: Bearer $TOKEN"
```

PowerShell:

```powershell
$login = Invoke-RestMethod -Method Post -Uri http://localhost:5100/api/v1/auth/login -ContentType 'application/json' `
  -Body '{"email":"<your e-mail>","password":"<your password>"}'
$headers = @{ authorization = "Bearer $($login.accessToken)" }
Invoke-RestMethod -Method Post -Uri http://localhost:<interview port>/api/v1/dev/grant-credits -Headers $headers `
  -ContentType 'application/json' -Body '{"quantity":1}'
Invoke-RestMethod -Uri http://localhost:<interview port>/api/v1/credits -Headers $headers
```

`<interview port>` is the interview-service port from the dashboard. Then start an interview in the browser: it consumes one credit. Set `interviews-require-credit` back to `false` when you are done.

### 9. Stop, reset, clean up

- **Stop**: Ctrl+C in the terminal running the AppHost. Aspire stops the containers it started.
- **What survives a stop**: the Postgres data in the Docker volume `exit-interview-agent-pgdata` (accounts, credits, ledger). **Open interview sessions do not survive a restart** (they are held in memory, ADR-0076); their credits are returned by the restart rule of the ledger (W11).
- **Reset the database** (deletes accounts, credits and all local records; dev data only): stop the stack, then `docker volume rm exit-interview-agent-pgdata`. Start again; the schema is migrated.
- **Signing key**: with the key from `setup.*` in user-secrets, tokens stay valid across restarts. Without it, the key changes on each run and tokens from before the restart stop working; sign in again.
- **Remove the secrets**: `dotnet user-secrets clear --project src/ExitInterviewAgent.AppHost` (removes every parameter, including the authservice key; run `setup.*` again to recreate the dev ones).

### 10. Troubleshooting

Keyed on what you see. The AppHost's own table, for the container and MCP cases, is in [`scripts/README.md`](../../scripts/README.md#troubleshooting-keyed-on-the-literal-text-you-will-see).

| You see | Cause | Fix |
|---|---|---|
| `Container runtime 'docker' could not be found.` | no container engine on `PATH` | install Docker Desktop, or Podman with `DOTNET_ASPIRE_CONTAINER_RUNTIME=podman` |
| `Cannot connect to the Docker daemon` | engine installed, not running | start it, then `docker info` |
| `pull access denied` or `unauthorized` for `ghcr.io/konradcinkusz/authservice` | the package is private to you | `docker login ghcr.io` with a PAT that has `read:packages` (section 5), or run with `Identity__Enabled=false` (then the web interview cannot run) |
| `pulling` stalls, or `429 Too Many Requests` from Docker Hub | anonymous pull limit | `docker login`, or wait |
| `address already in use` or a bind error on 3000, 5050 or 5100 | another program holds the port (these three are fixed) | find the owner with `lsof -i :<port>` (Linux/macOS) or `Get-NetTCPConnection -LocalPort <port>` (Windows), stop it by its process id, then start again |
| interview-service `/health` is `DEGRADED`: `interview-model` | the mock model runs (expected), or a real provider is set but its model is missing or wrong | section 7; the detail line gives the reason |
| `503 interviews_disabled` | the emergency switch is off, no provider resolves, or the **daily start cap** (20 per UTC day across all accounts) is reached | read the `detail` on `/health`; the cap resets at midnight UTC |
| `402 payment_required` | the credit gate is on and the account has no credit | section 8 (grant), or set `interviews-require-credit` back to `false` |
| `429 rate_limited` | the local limits: 5 starts per account per hour, 20 per IP per hour, 30 replies per minute per account | wait; they are counted in memory, so a restart of the service clears them |
| `403 email_not_verified` | the verified-email gate is on in the service you are calling (a run outside the AppHost, or changed settings) | check the `Interviews__RequireVerifiedEmail` value in the dashboard; the AppHost sets `false` |
| `401` on `/api/v1/...` | no token, an expired token, or identity is off | sign in again; with `Identity__Enabled=false` every such call is 401 |
| `409 interview_in_progress` | the account already has an open interview | finish it or delete it |
| `initdb: error: directory "/var/lib/postgresql/data" exists but is not empty` | the volume was created by another Postgres major version | `docker volume rm exit-interview-agent-pgdata` (dev data only), then start again |

### 11. What was and was not run

Authored on 2026-10-11 in an environment with no Docker daemon and no .NET SDK. "Read" means the claim was checked in the source, the configuration or the published package named.

| Item | State |
|---|---|
| Setup scripts: what they do (sections 2) | read in `scripts/setup.sh` and `scripts/setup.ps1`; **NOT RUN** (needs Docker for step 1 of the AppHost; `setup.ps1` needs PowerShell, not installed here) |
| AppHost: resources, ports, parameters (sections 3, 7, 8) | read in `src/ExitInterviewAgent.AppHost/Program.cs`; **NOT RUN** (the AppHost starts containers; Docker is not running here) |
| pgAdmin default e-mail and server registration (section 4) | read in the Aspire PostgreSQL 13.6.0 package (`PGADMIN_DEFAULT_EMAIL`, `WritePgAdminServerJson`); **NOT RUN** in pgAdmin itself |
| authservice `v0.3.4`: registration, login, consent versions, email confirmation (sections 5, 6) | read in the tag `v0.3.4` of `konradcinkusz/authservice`; **NOT RUN** (the image was not pulled) |
| The web flow in section 6 (labels, steps, consent) | read in `web/app` and its message catalogue; **NOT RUN** in a browser |
| The interview-service endpoints, codes and limits (sections 6, 8, 10) | read in `src/ExitInterviewAgent.InterviewService` and `src/ExitInterviewAgent.Contracts`; **NOT RUN** against a live service |
| `scripts/smoke-local.sh`: syntax, argument checks, failure path against closed ports | **run** here (`bash -n`, `--dry-run`, and a run against closed ports: 3 FAIL lines, exit 1) |
| `scripts/smoke-local.sh` against a live stack, with and without a token | **NOT RUN** (needs the stack) |
| `scripts/smoke-local.ps1` | **NOT RUN** (no PowerShell here) |
| Image pull of `ghcr.io/konradcinkusz/authservice:v0.3.4` | **NOT RUN** (ADR-0003: the sandbox proxy refuses image blobs) |
| A real model answering an interview | **NOT RUN** (no key, and the cost is the owner's decision; W9) |
| Payment path with the grant (section 8) | read in the code; **NOT RUN** |

Whether the L6 work (Aspire tests and CI) changes this: if CI starts the AppHost, that would be the first automated run of this stack. Until a run like that is recorded, every row above that says NOT RUN stays NOT RUN.

---

## Polski

### 1. Czego potrzebujesz

| Narzędzie | Wersja | Po co |
|---|---|---|
| .NET SDK | ta z [`global.json`](../../global.json) (10.x, roll-forward w obrębie gałęzi funkcji) | buduje i uruchamia AppHost oraz usługi |
| Docker Desktop albo Podman | działający demon | Postgres, pgAdmin i authservice działają jako kontenery |
| Node.js | 22 lub nowszy | aplikacja web |
| pnpm | wersja używana przez aplikację web ([`web/app/package.json`](../../web/app/package.json)); `corepack enable` go instaluje | menedżer pakietów aplikacji web |
| git | dowolny aktualny | klon i hook skanowania sekretów przy commicie |

Windows: polecenia uruchamiaj w PowerShell. Ścieżki poniżej mają `/`; PowerShell przyjmuje oba znaki.

### 2. Konfiguracja jednorazowa: `scripts/setup.sh` albo `scripts/setup.ps1`

Najpierw `scripts/setup.sh --check` (albo `scripts/setup.ps1 -Check`). Tylko raportuje, czego brakuje, i niczego nie zmienia.

Potem `scripts/setup.sh` (albo `scripts/setup.ps1`). Co robi, krok po kroku, zgodnie z kodem:

1. **Wymagania**: git, dotnet, node, pnpm, działający demon Dockera. `setup.sh` sprawdza też, czy `dotnet --version` zaczyna się od `10.`. Zatrzymuje się, jeśli czegoś brakuje.
2. **Hook gita**: ustawia `core.hooksPath` na `scripts/hooks`, więc każdy commit przechodzi skanowanie sekretów.
3. **Lokalny magazyn sekretów** (`dotnet user-secrets`, nigdy plik w drzewie):
   - tworzy **tylko deweloperski** klucz podpisujący RSA-2048 dla authservice (`Parameters:authservice-jwt-private-key`). `setup.sh` używa `openssl`; `setup.ps1` używa środowiska .NET. Gdy klucza brak, AppHost tworzy jednorazowy na każdy start.
   - tworzy tylko deweloperski sekret klienta MCP i klucz szyfrowania tokenów authservice. Używane są tylko wtedy, gdy skonfigurowano ścieżkę MCP (patrz `scripts/README.md`).
4. **Opcjonalne integracje**: tekst wypisywany przez skrypt mówi, że żadna integracja opcjonalna nie jest podłączona. To jest nieaktualne: ustawienia modelu i płatności z sekcji 7 i 8 AppHost czyta z user-secrets, nie skrypt konfiguracji. Skrypt ich nie rusza.

Ponowne uruchomienie jest bezpieczne: istniejące sekrety zostają.

### 3. Start stosu

```bash
dotnet run --project src/ExitInterviewAgent.AppHost
```

Pierwsze uruchomienie pobiera obrazy (Postgres 17, pgAdmin, przypięty obraz authservice) i buduje usługi, więc trwa dłużej. Konsola wypisuje wtedy adres **Aspire dashboard**, z tokenem logowania (parametr `?t=`). Otwórz tę linię: to jedyne miejsce z listą wszystkich zasobów, ich stanem, logami i endpointami. Port dashboardu podaje Aspire; w tym repozytorium nie jest stały.

Co startuje i pod jakim adresem na Twoim komputerze:

| Zasób | Adres na Twoim komputerze | Stały? | Uwagi |
|---|---|---|---|
| web (Next.js + BFF) | http://localhost:3000 | tak (3000) | płatność odsyła tu płacącego, więc port nie może się zmieniać |
| authservice (`ghcr.io/konradcinkusz/authservice:v0.3.4`) | http://localhost:5100 | tak (5100) | adres JWKS i wystawcy, z którego korzystają interview-service i aplikacja web |
| pgAdmin | http://localhost:5050 | tak (5050) | tylko ten komputer (sekcja 4) |
| interview-service | port pokazany w dashboardzie | nie (przydziela Aspire) | `/health` wymienia integracje; skrypt smoke potrzebuje tego portu |
| postgres 17 | port pokazany w dashboardzie | nie | jeden serwer, dwie bazy: `interviewdb` (interview-service) i `authdb` (authservice); dane w wolumenie Dockera `exit-interview-agent-pgdata` |

AppHost wypisuje tę samą listę w bannerze startowym, a `GET <interview-service>/health` podaje każdą integrację z `configured` true albo false i powodem.

### 4. pgAdmin: przeglądanie bazy

1. Otwórz http://localhost:5050.
2. **E-mail logowania**: `admin@domain.com`. To domyślna wartość, którą pakiet Aspire PostgreSQL (13.6.0) ustawia dla pgAdmin (`PGADMIN_DEFAULT_EMAIL`). Jeśli ją zmieniłeś, używaj swojej.
3. **Hasło**: parametr AppHost `pgadmin-password`. Ustaw własne, żeby je znać:

   ```bash
   dotnet user-secrets set "Parameters:pgadmin-password" "<hasło, które wybierasz>" --project src/ExitInterviewAgent.AppHost
   ```

   Bez tego AppHost generuje hasło na każdy start; nie jest zapisane w miejscu, do którego masz później dostęp, więc ustaw własne. Po zmianie zrestartuj stos.
4. **Serwer**: pakiet Aspire zapisuje serwer postgres do pgAdmin automatycznie (krok `WritePgAdminServerJson`). Rozwiń serwer w drzewie po lewej i otwórz bazy `interviewdb` oraz `authdb`.
5. **Hasło do bazy**: jeśli pgAdmin pyta o hasło postgres, weź je z zasobu **postgres** w dashboardzie, który pokazuje jego connection string. Hasło jest generowane na każdy start.

Dane rozmów są tylko w `interviewdb` (sesje żyją w pamięci, patrz sekcja 6). Rejestr, kredyty i zakupy są w `interviewdb`, a konta w `authdb`.

### 5. authservice: obraz i jak go pobrać

AppHost uruchamia `ghcr.io/konradcinkusz/authservice:v0.3.4`, przypięty tag; nigdy `latest` (ADR-0003). Obraz sam wykonuje migracje (`Database__SchemaMode=Migrate`) i ma własną bazę `authdb`.

**Pobieranie obrazu.** Jeśli pobranie zostanie odrzucone (`pull access denied` albo `unauthorized`), pakiet jest dla Ciebie prywatny. Zaloguj się tokenem dostępu osobistego GitHub z uprawnieniem `read:packages`:

```bash
echo "<twój token>" | docker login ghcr.io -u <twój użytkownik GitHub> --password-stdin
```

Niezweryfikowane tutaj: to środowisko nie pobrało obrazu (ADR-0003 odnotowuje odpowiedzi 403 od proxy sandboxa), więc czy pobranie wymaga logowania, pozostaje otwarte, dopóki ktoś tego nie uruchomi.

**Bez obrazu albo bez pobrania: uruchom bez tożsamości.** Start z `Identity:Enabled=false` (AppHost czyta to z konfiguracji, więc działa zmienna środowiskowa `Identity__Enabled=false`):

```bash
Identity__Enabled=false dotnet run --project src/ExitInterviewAgent.AppHost
```

Wtedy chronione endpointy odpowiadają 401, `/health` mówi `identity: not configured`, a **wywiad webowy nie działa** (każde wywołanie `/api/v1` dostaje 401). Używaj tego tylko do sprawdzenia reszty stosu.

**Potwierdzenie e-mail nie jest potrzebne lokalnie.** AppHost nie przekazuje klucza SendGrid. W authservice `v0.3.4` potwierdzenie e-mail jest wymagane tylko wtedy, gdy klucz SendGrid jest ustawiony (`requireConfirmedEmail = canSendEmail` w `Program.cs`), a nowe konto jest potwierdzane od razu, gdy go brak (`EmailConfirmed = !requireVerification` w endpointcie rejestracji). Możesz więc założyć konto i się zalogować bez maila. Claim `email_verified` **nie ma** w `v0.3.4` (jest na gałęzi main authservice, PR #73, i wymaga nowego wydania obrazu przez jego właściciela). Dlatego AppHost ustawia `Interviews__RequireVerifiedEmail=false` (ADR-0079).

### 6. Wywiad na mocku, krok po kroku (bez klucza, bez pieniędzy)

Stos startuje z **mockiem** (zaskryptowany model): bez połączenia z siecią, bez kosztów. Jego treść jest zaskryptowana, nie generowana, więc tutaj oceniasz przepływ, a nie jakość (do jakości służy sekcja 7).

1. **Sprawdź stos.** `GET <interview-service>/health` pokazuje `interview-model` jako nieskonfigurowany, a w szczegółach nazwany jest mock. To oczekiwane.
2. **Załóż konto.** Otwórz http://localhost:3000/register. Zaakceptuj regulamin i politykę prywatności (oba są wymagane), potem **Create account**. Strony konta są tylko po angielsku (jak w [README](../../README.md), wiersz panelu webowego), więc etykiety podaję po angielsku. Rejestracja lokalnie nie wysyła maila; konto jest potwierdzane od razu (sekcja 5).
3. **Zaloguj się.** http://localhost:3000/login, potem **Sign in** (etykieta po angielsku, jak wyżej).
4. **Zaakceptuj aktualne warunki** na stronie zgody (`/consent`), gdy aplikacja o to poprosi. Bez tego nie pokaże wywiadu.
5. **Start.** Otwórz http://localhost:3000/interview. Zaznacz pole, że przeczytałeś warunki i zgadzasz się na rozmowę z AI, potem **Rozpocznij rozmowę** (EN: *Start the interview*). Rozmówca na początku mówi, że jest AI.
6. **Rozmowa.** Pisz w polu i naciśnij **Wyślij** (Enter wysyła, Shift+Enter daje nową linię). Odpowiedz na kilka pytań. Nie podawaj prawdziwych nazwisk osób ani nazwy firmy; rozmowa służy do sprawdzenia przepływu.
7. **Wynik.** Gdy rozmowa się kończy, strona pokazuje **Zapis i szkice tekstów**: zapis, szkice tekstów (kafelki), przycisk kopiowania każdego tekstu i pobranie `tiles.html`. Wynik może chwilę potrwać: strona pokazuje komunikat ładowania, aż kafelki będą gotowe.
8. **Usunięcie.** Użyj **Usuń wszystko teraz** na stronie wyniku (potwierdź **Tak, usuń**, jeśli zapyta), albo **Przerwij i usuń** w trakcie rozmowy. Aplikacja pokazuje wtedy, że nic z rozmowy nie jest zachowywane. Usunięcie jest zawsze dostępne (tak mówi tekst zgody).
9. **Kolejna rozmowa**: **Rozpocznij kolejną rozmowę**, albo wyloguj się i zaloguj ponownie.

Reguły, które spotkasz, zgodnie z kodem: jedna otwarta rozmowa na konto (drugi start daje 409 `interview_in_progress`); zakończona rozmowa jest czytelna 30 minut, potem jest usuwana; rozmowa bez aktywności wygasa po 30 minutach.

### 7. Prawdziwy model (klucz API Anthropic, płacony przez Ciebie)

**Kosztuje.** Każda rozmowa jest rozliczana na konto klucza, który podasz. Przed użyciem prawdziwego klucza ustaw limit wydatków w konsoli dostawcy i użyj klucza przeznaczonego tylko dla tego projektu.

Wartości trafiają do user-secrets. Nigdy nie wklejaj ich do pliku, commitu ani czatu:

```bash
dotnet user-secrets set "Parameters:anthropic-api-key" "<twój klucz>" --project src/ExitInterviewAgent.AppHost
dotnet user-secrets set "Parameters:interviews-model" "<identyfikator modelu z konsoli dostawcy>" --project src/ExitInterviewAgent.AppHost
# opcjonalnie: identyfikator workspace z konsoli dostawcy (nie jest sekretem)
dotnet user-secrets set "Parameters:anthropic-workspace-id" "<identyfikator workspace>" --project src/ExitInterviewAgent.AppHost
```

PowerShell przyjmuje te same polecenia. Zachowaj cudzysłowy; ścieżka może mieć `\` albo `/`.

Co robią te ustawienia, wg AppHost:

- `anthropic-api-key` (sekret): gdy klucz jest ustawiony, dostawca domyślnie to Anthropic, a klucz trafia do interview-service jako `ANTHROPIC_API_KEY`. Bez klucza dostawca domyślnie to mock.
- `interviews-model`: **dla prawdziwego dostawcy nie ma wartości domyślnej.** Ustaw identyfikator modelu z konsoli dostawcy. Repozytorium nie podaje nazwy modelu; brakujący lub błędny identyfikator daje `interview-model` jako DEGRADED z powodem, a rozmowy odpowiadają 503.
- `interviews-provider`: wymusza dostawcę (`anthropic` albo `mock`).

Po zmianie user-secrets zrestartuj stos (AppHost czyta je przy starcie). Potem:

- `GET <interview-service>/health`: `interview-model` skonfigurowany, z komunikatem `provider 'anthropic' is configured; the key is never reported`. Klucz nigdy nie jest pokazywany.
- Przeprowadź rozmowę z sekcji 6. Oceń odpowiedzi sam. Wynik na mocku nic nie mówi o jakości, a w tym repozytorium nie ma jeszcze wyniku jakości z prawdziwym modelem (otwarte zadanie W9 w [planie aplikacji webowej](../architecture/web-app-plan.md)).

Powrót do mocka: usuń klucz poleceniem `dotnet user-secrets remove "Parameters:anthropic-api-key" --project src/ExitInterviewAgent.AppHost` i zrestartuj.

### 8. Ścieżka płatności lokalnie (bez pieniędzy)

Domyślnie starty są bezpłatne (`interviews-require-credit` ma wartość `false`). Żeby przećwiczyć bramkę kredytów:

1. Włącz bramkę i zrestartuj:

   ```bash
   dotnet user-secrets set "Parameters:interviews-require-credit" "true" --project src/ExitInterviewAgent.AppHost
   ```

2. Start rozmowy bez kredytu: API odpowiada **402 `payment_required`**, a strona webowa oferuje **Kup jedną rozmowę**.
3. **Kup jedną rozmowę nie dodaje kredytu lokalnie.** Deweloperski dostawca płatności od razu odsyła na adres sukcesu (z `?checkout=fake`) i nie wysyła webhooka. Kredyt powstaje tylko z podpisanego zdarzenia płatniczego, którego lokalny stos nie wysyła. Użyj zamiast tego nadania deweloperskiego.
4. **Nadanie kredytów (tylko deweloperskie)**: `POST /api/v1/dev/grant-credits` z `{"quantity": 1}` (od 1 do 10). Potrzebny jest token konta (przeglądarka trzyma token w ciasteczku HTTP-only, więc token zdobywasz logując się przez API, poniżej). Trasa istnieje tylko wtedy, gdy środowisko to Development, dostawca płatności to fake, a checkout jest skonfigurowany; AppHost ustawia wszystkie trzy. Wszędzie indziej odpowiada 404. Nadanie zapisuje się w rejestrze jako `dev-grant:`, więc jest widocznie czymś innym niż zdarzenie dostawcy.

Zdobycie tokenu (podaj e-mail i hasło swojego konta; to endpoint logowania authservice):

```bash
TOKEN=$(curl -sS -X POST http://localhost:5100/api/v1/auth/login -H 'content-type: application/json' \
  -d '{"email":"<twój e-mail>","password":"<twoje hasło>"}' | python3 -c 'import json,sys; print(json.load(sys.stdin)["accessToken"])')
curl -sS -X POST http://localhost:<port interview>/api/v1/dev/grant-credits -H "authorization: Bearer $TOKEN" \
  -H 'content-type: application/json' -d '{"quantity":1}'
curl -sS http://localhost:<port interview>/api/v1/credits -H "authorization: Bearer $TOKEN"
```

PowerShell:

```powershell
$login = Invoke-RestMethod -Method Post -Uri http://localhost:5100/api/v1/auth/login -ContentType 'application/json' `
  -Body '{"email":"<twój e-mail>","password":"<twoje hasło>"}'
$headers = @{ authorization = "Bearer $($login.accessToken)" }
Invoke-RestMethod -Method Post -Uri http://localhost:<port interview>/api/v1/dev/grant-credits -Headers $headers `
  -ContentType 'application/json' -Body '{"quantity":1}'
Invoke-RestMethod -Uri http://localhost:<port interview>/api/v1/credits -Headers $headers
```

`<port interview>` to port interview-service z dashboardu. Potem rozpocznij rozmowę w przeglądarce: zużyje jeden kredyt. Gdy skończysz, ustaw `interviews-require-credit` z powrotem na `false`.

### 9. Zatrzymanie, reset, sprzątanie

- **Zatrzymanie**: Ctrl+C w terminalu z AppHost. Aspire zatrzymuje kontenery, które uruchomił.
- **Co przetrwa zatrzymanie**: dane Postgresa w wolumenie Dockera `exit-interview-agent-pgdata` (konta, kredyty, rejestr). **Otwarte sesje rozmów nie przetrwają restartu** (trzymane są w pamięci, ADR-0076); ich kredyty wraca reguła restartu w rejestrze (W11).
- **Reset bazy** (kasuje konta, kredyty i wszystkie lokalne rekordy; tylko dane deweloperskie): zatrzymaj stos, potem `docker volume rm exit-interview-agent-pgdata`. Po starcie schemat zostanie zmigrowany od nowa.
- **Klucz podpisujący**: z kluczem z `setup.*` w user-secrets tokeny zostają ważne po restartach. Bez niego klucz zmienia się przy każdym starcie i tokeny sprzed restartu przestają działać; zaloguj się ponownie.
- **Usunięcie sekretów**: `dotnet user-secrets clear --project src/ExitInterviewAgent.AppHost` (usuwa wszystkie parametry, w tym klucz authservice; uruchom `setup.*` ponownie, żeby odtworzyć deweloperskie).

### 10. Rozwiązywanie problemów

Według tego, co widzisz. Tabela AppHost dla kontenerów i ścieżki MCP jest w [`scripts/README.md`](../../scripts/README.md#troubleshooting-keyed-on-the-literal-text-you-will-see).

| Widzisz | Przyczyna | Co zrobić |
|---|---|---|
| `Container runtime 'docker' could not be found.` | brak silnika kontenerów w `PATH` | zainstaluj Docker Desktop albo Podman z `DOTNET_ASPIRE_CONTAINER_RUNTIME=podman` |
| `Cannot connect to the Docker daemon` | silnik zainstalowany, nie działa | uruchom go, potem `docker info` |
| `pull access denied` albo `unauthorized` dla `ghcr.io/konradcinkusz/authservice` | pakiet jest dla Ciebie prywatny | `docker login ghcr.io` tokenem z `read:packages` (sekcja 5), albo `Identity__Enabled=false` (wtedy wywiad webowy nie działa) |
| `pulling` się zawiesza albo `429 Too Many Requests` z Docker Hub | limit pobrań bez logowania | `docker login` albo poczekaj |
| `address already in use` albo błąd bindowania na 3000, 5050 lub 5100 | inny program zajmuje port (te trzy są stałe) | znajdź właściciela `lsof -i :<port>` (Linux/macOS) albo `Get-NetTCPConnection -LocalPort <port>` (Windows), zatrzymaj go po identyfikatorze procesu, potem uruchom ponownie |
| `/health` interview-service `DEGRADED`: `interview-model` | działa mock (oczekiwane), albo ustawiono prawdziwego dostawcę bez poprawnego modelu | sekcja 7; wiersz szczegółów podaje powód |
| `503 interviews_disabled` | wyłącznik awaryjny wyłączony, żaden dostawca nie działa, albo osiągnięto **dzienny limit startów** (20 na dzień UTC, wszystkie konta) | przeczytaj `detail` w `/health`; limit zeruje się o północy UTC |
| `402 payment_required` | bramka kredytów włączona, a konto nie ma kredytu | sekcja 8 (nadanie) albo ustaw `interviews-require-credit` z powrotem na `false` |
| `429 rate_limited` | lokalne limity: 5 startów na konto na godzinę, 20 na IP na godzinę, 30 odpowiedzi na minutę na konto | poczekaj; liczniki są w pamięci, restart usługi je zeruje |
| `403 email_not_verified` | bramka potwierdzonego e-maila jest włączona w usłudze, do której dzwonisz (uruchomienie poza AppHost albo zmienione ustawienia) | sprawdź wartość `Interviews__RequireVerifiedEmail` w dashboardzie; AppHost ustawia `false` |
| `401` na `/api/v1/...` | brak tokenu, token wygasł albo tożsamość wyłączona | zaloguj się ponownie; przy `Identity__Enabled=false` każde takie wywołanie daje 401 |
| `409 interview_in_progress` | konto ma już otwartą rozmowę | dokończ ją albo ją usuń |
| `initdb: error: directory "/var/lib/postgresql/data" exists but is not empty` | wolumen został utworzony przez inną wersję główną Postgresa | `docker volume rm exit-interview-agent-pgdata` (tylko dane deweloperskie), potem start |

### 11. Co uruchomiono, a czego nie

Spisano 2026-10-11 w środowisku bez demona Dockera i bez .NET SDK. „Odczytano" znaczy, że twierdzenie sprawdzono w kodzie, konfiguracji lub wskazanym opublikowanym pakiecie.

| Pozycja | Stan |
|---|---|
| Skrypty konfiguracji: co robią (sekcja 2) | odczytano w `scripts/setup.sh` i `scripts/setup.ps1`; **NOT RUN** (krok 1 AppHost wymaga Dockera; `setup.ps1` wymaga PowerShell, którego tu nie ma) |
| AppHost: zasoby, porty, parametry (sekcje 3, 7, 8) | odczytano w `src/ExitInterviewAgent.AppHost/Program.cs`; **NOT RUN** (AppHost uruchamia kontenery; Docker tu nie działa) |
| Domyślny e-mail pgAdmin i rejestracja serwera (sekcja 4) | odczytano w pakiecie Aspire PostgreSQL 13.6.0 (`PGADMIN_DEFAULT_EMAIL`, `WritePgAdminServerJson`); **NOT RUN** w samym pgAdmin |
| authservice `v0.3.4`: rejestracja, logowanie, wersje zgód, potwierdzenie e-mail (sekcje 5, 6) | odczytano w tagu `v0.3.4` repozytorium `konradcinkusz/authservice`; **NOT RUN** (obrazu nie pobrano) |
| Przepływ webowy z sekcji 6 (etykiety, kroki, zgoda) | odczytano w `web/app` i katalogu komunikatów; **NOT RUN** w przeglądarce |
| Endpointy, kody i limity interview-service (sekcje 6, 8, 10) | odczytano w `src/ExitInterviewAgent.InterviewService` i `src/ExitInterviewAgent.Contracts`; **NOT RUN** na działającej usłudze |
| `scripts/smoke-local.sh`: składnia, sprawdzenie argumentów, ścieżka błędu przy zamkniętych portach | **uruchomiono** tutaj (`bash -n`, `--dry-run` i przebieg przy zamkniętych portach: 3 linie FAIL, kod 1) |
| `scripts/smoke-local.sh` na działającym stosie, z tokenem i bez | **NOT RUN** (potrzebny stos) |
| `scripts/smoke-local.ps1` | **NOT RUN** (brak PowerShell) |
| Pobranie obrazu `ghcr.io/konradcinkusz/authservice:v0.3.4` | **NOT RUN** (ADR-0003: proxy sandboxa odrzuca bloby obrazu) |
| Prawdziwy model odpowiadający w rozmowie | **NOT RUN** (brak klucza; koszt to decyzja właściciela; W9) |
| Ścieżka płatności z nadaniem (sekcja 8) | odczytano w kodzie; **NOT RUN** |

Czy praca L6 (testy Aspire i CI) to zmienia: jeśli CI uruchomi AppHost, będzie to pierwsze zautomatyzowane uruchomienie tego stosu. Dopóki takie uruchomienie nie zostanie zapisane, każdy wiersz oznaczony NOT RUN pozostaje NOT RUN.
