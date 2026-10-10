# Web interview app: deployment runbook

Status: **written, NOT deployed.** Nothing in this repository deploys anything: no Fly app exists, no secret is set, no tag is pushed (AGENTS.md, PROJECT-BRIEF §2). The `flyio/*.fly.toml` files and `.github/workflows/flyio.yml` are generated topology, syntax-checked and not run. The steps below are for the owner, in the order given. Every command is an **example**: check it against the Fly CLI you have installed. Every value in angle brackets (`<...>`) is a placeholder, never a real key.

Languages: **English** first, then **Polski** (below).

Related: [cost controls](web-app-cost-controls.md) (the four brakes and the spend limit), [secrets](../../flyio/SECRETS.md), [release gate](../release/RELEASE-GATE.md) (every item below that is a gate is listed there as NOT RUN until you change it), [plan](../architecture/web-app-plan.md).

---

## English

### 0. Before you start

- **No paid interview before the gates in [RELEASE-GATE.md](../release/RELEASE-GATE.md) that concern money and law are closed by you:** the legal review (item 16), the Stripe sandbox test (item 17), the real-model run on 10 Polish interviews with a measured cost (item 18), the spend limit (item 19). A staging deployment with the switch on and no payment configured is allowed earlier; it is not a launch.
- **Staging = the `dev` environment in this repository.** The Fly apps are named `exit-interview-agent-<name>-dev` (see the `[app]` lines in `flyio/*.fly.toml`). The first deployment is that environment. A separate production environment is a later decision; do not rename the apps, because `flyio.yml` creates them by these names.

### 1. Fly account and organisation

1. Create or sign in to the Fly.io account that will own the apps. Use the owner's account, not a shared one.
2. Install the Fly CLI on your machine and sign in: `fly auth login` (example). Do this yourself; no token is stored in the repository or in GitHub by these steps.
3. Confirm the organisation the workflow expects (`FLY_ORG: personal` in `flyio.yml`), or change it in one place if you use another.

### 2. Create the apps (examples, not run)

The `flyio.yml` workflow creates missing apps idempotently when a tag is pushed. Until a deployment is decided, the guide prefers the pipeline (FLY-IO-DEPLOYMENT §11); the commands below are the documented fallback, run once, and recorded:

```bash
fly apps create exit-interview-agent-postgres --org personal
fly apps create exit-interview-agent-authservice-dev --org personal
fly apps create exit-interview-agent-interview-service-dev --org personal
fly apps create exit-interview-agent-web-dev --org personal
fly volumes create exit_interview_agent_pgdata --app exit-interview-agent-postgres --region fra --size 3
```

The database volume is created once, in the region the apps use (`fra`). Do not add a second database machine: it would get a second, empty volume (`--ha=false` in the workflow).

### 3. Secrets: what to set, where

Secrets are set with `fly secrets set`, never in a `.toml` file, a commit, a PR, a chat or a document. Set them with `--stage` so the machines restart once, at deploy. Check the names with `fly secrets list` (it shows names and digests only).

> **Deviation from the standard, to close before the first real deployment.** The standards guide (FLY-IO-DEPLOYMENT §9, §11) says secrets and apps are set from the pipeline, not by hand, so environments cannot drift. `flyio.yml` does this for the database, authservice and the interview service's connection string only. The provider key, the Anthropic workspace id, the model, the Stripe keys and the price are **not** in the workflow yet. Until they are, the commands in this section are the one-time manual bootstrap: run them once, record in the release gate that you did, and move the values into the workflow (as GitHub `dev` environment secrets) in a separate change. Do not keep them only in your shell history.

**Interview service** (`exit-interview-agent-interview-service-dev`):

```bash
fly secrets set --app exit-interview-agent-interview-service-dev --stage \
  "ConnectionStrings__interviewdb=<set by the pipeline from INTERVIEW_DB_PASSWORD, see flyio/SECRETS.md>" \
  "Ledger__ActiveKeyId=<key id, 1-32 characters>" \
  "Ledger__Keys__0__Id=<same key id>" \
  "Ledger__Keys__0__Secret=<openssl rand -base64 32>" \
  "ANTHROPIC_API_KEY=<provider key from the dedicated workspace, see step 4>" \
  "Interviews__Provider=anthropic" \
  "Interviews__Model=<model id chosen after the W9 run>" \
  "ANTHROPIC_WORKSPACE_ID=<workspace id from the provider console>"
```

- `ANTHROPIC_API_KEY` is the default name of the variable that holds the key (`Interviews:ApiKeyEnv`). If you rename it, set `Interviews__ApiKeyEnv` to the new name as well.
- **The workspace identifier is not read by this code yet.** [cost controls §2](web-app-cost-controls.md) says so. Set it only when the code reads it; until then the spend limit in the console is the control that matters.
- `Interviews__Model` is a configuration value, not a repository value: the model is chosen after the W9 run, and no model identifier is written into the repository.

**Payments** (same app), added in step 5, only after the Stripe test-mode checks:

```bash
fly secrets set --app exit-interview-agent-interview-service-dev --stage \
  "Billing__Provider=stripe" \
  "Billing__StripeSecretKey=<sk_test_... from the Stripe TEST mode dashboard>" \
  "Billing__StripeWebhookSecret=<whsec_... of the test-mode webhook endpoint>" \
  "Billing__StripePriceId=<price_... of one credit, test mode>" \
  "Billing__PriceMinorUnits=<price in minor units, decided after W9>" \
  "Billing__Currency=<three-letter lower-case code, decided by the owner>" \
  "Billing__SuccessUrl=https://<your domain>/<success page>" \
  "Billing__CancelUrl=https://<your domain>/<cancel page>"
```

- **Price and currency are your decision** (web-app-plan §4, §9). The code has no default and refuses checkout (503) until both are set. Do not guess them.
- Use a `sk_test_` key in staging. Switching to a live key is a separate, later step with its own gate item.

**Web app** (`exit-interview-agent-web-dev`): no secret. The BFF holds no key; it verifies tokens against authservice's public keys.

**Authservice** (`exit-interview-agent-authservice-dev`): the secrets in `flyio/SECRETS.md` (signing key, database connection, client secret, encryption key). The signing key is held by authservice only.

### 4. Provider console: a dedicated workspace and a spend limit

This is the only control that stops the bill (cost controls §2). The console's menus change; check each step against the screen you see.

1. Create a **separate workspace** for this app. Do not reuse a workspace that other work uses.
2. Create the API key in that workspace and put it only in `ANTHROPIC_API_KEY` (step 3).
3. Set a **monthly spend limit** on the workspace. The amount comes from the measured cost of one interview (W9) times the interviews you can afford. Until W9 exists, leave staging with the switch off for real users.
4. Set the limit's notification to an address you read.
5. Record the workspace name and the date of the limit in [RELEASE-GATE.md](../release/RELEASE-GATE.md) item 19, not the key.

### 5. Stripe: test mode first, sandbox test required

1. Use the **test mode** dashboard only, in staging. Create one product with one price (one credit), and a webhook endpoint `https://<your domain>/api/v1/webhooks/payments`. Copy the test keys into step 3.
2. The webhook **event format has not been checked against the real Stripe API** (the tests use a stub). Before any real payment, run the sandbox test in [RELEASE-GATE.md](../release/RELEASE-GATE.md) item 17: a test checkout, a signed test event delivered to the endpoint, the credit added once (send the same event twice; the balance must not grow twice), a wrong signature refused, and an amount that does not match the price refused.
3. Only after that, and after the legal review, consider live keys. This runbook does not cover live mode.

### 6. authservice must emit `email_verified`

The verified-email gate (`Interviews:RequireVerifiedEmail`, production: `true`) reads the `email_verified` claim from the access token. **authservice does not put this claim in the token yet** (ADR-0078). Until that change is made and deployed:

- every interview start is refused with `403 email_not_verified`. That is the intended fail-closed behaviour, and `/health` shows the gate.
- **Do not set `Interviews__RequireVerifiedEmail=false` in production to make starts work.** It removes one of the four brakes.

### 7. Domain and TLS

1. Choose the domain (the owner's). Point it at the web app: `fly certs add <your domain> --app exit-interview-agent-web-dev` (example), then add the DNS record Fly shows. Fly issues the certificate; the apps use `force_https`.
2. Use the same domain in `Billing__SuccessUrl`, `Billing__CancelUrl` and the Stripe webhook endpoint. A mismatch means a user returns to a page that does not exist, or a webhook never arrives.
3. Check the certificate in a browser before sharing the address.

### 8. Deploy and check `/health`

Deployment is by the workflow (after a `v*` tag, which the owner pushes when the gates allow) or by `fly deploy --config flyio/<app>.fly.toml --app <app> --image <image>` (example). Then check, in this order:

1. Web: `https://<your domain>/healthz` answers 200 (the probe in `web.fly.toml`).
2. Interview service: `https://exit-interview-agent-interview-service-dev.fly.dev/health`. It lists the integrations by name, including **`interviews`** and **`billing`**, with their state and the reason when they are degraded. The state you should expect at first is: `interviews` degraded until the provider and key are set (step 3), `billing` degraded until Stripe is set (step 5), and the verified-email gate refusing starts until step 6. A degraded integration is correct; a healthy one with a missing gate is not.
3. A start request with no credit returns `402 payment_required`, and with the switch off, `503 interviews_disabled`. Both are the expected answers before the gates are closed.

### 9. Emergency switch (kill switch)

To refuse **new** interviews at once:

```bash
fly secrets set --app exit-interview-agent-interview-service-dev "Interviews__Enabled=false"
```

This restarts the machines. New starts then answer `503 interviews_disabled`. A running interview finishes. The switch does not refund anything and does not change the spend limit.

To stop the spend itself: revoke or disable the workspace key in the provider console (running interviews then fail and their credits are returned, in the sessions that are not already completed). Then record the time and the reason in the release gate.

### 10. Rollback

- **Code:** list the releases with `fly releases --app <app>` and redeploy the previous image: `fly deploy --app <app> --image ghcr.io/konradcinkusz/exit-interview-agent-<name>:<previous tag>` (example). Each image is tagged by its release, so the previous one is still in GHCR.
- **Database:** schema migrations are **not** reverted by a code rollback. If a migration is the problem, restore the PostgreSQL volume from a backup taken before the deployment; take that backup before step 8, not after.
- **Secrets:** `fly secrets unset` removes one name; the old value is gone, so keep the new values recorded in your password manager, not in a file in the repository.

### 11. After a restart

Interview sessions live **in memory** (web-app-plan §3, ADR-0076). A deploy, a scale change, a machine replacement or a crash ends every open session:

- the person's next request gets `410 gone`, and the session's transcript is gone (there is no copy to recover, by design);
- **known gap:** the credit of an open session is **not** returned automatically on a restart. The ledger returns it only through the work tracked as W11. Until W11 is done, decide each case by hand: check the ledger for credits consumed without a completed result, and refund through the payment provider only where the session was lost by the service. Do not reconstruct a person's interview from any log: none contains interview text.
- to avoid the gap: do not deploy or restart while interviews may be running. No metric in the list (cost controls §4) gives the number of open sessions, so choose a quiet time from the usage in the provider console and the starts count, and say so in the release gate notes.

---

## Polski

### 0. Zanim zaczniesz

- **Żadna płatna rozmowa przed zamknięciem bramek z [RELEASE-GATE.md](../release/RELEASE-GATE.md), które dotyczą prawa i pieniędzy, przez właściciela:** przegląd prawny (pozycja 16), test płatności w piaskownicy Stripe (pozycja 17), przebieg na prawdziwym modelu, 10 wywiadów po polsku, z zmierzonym kosztem (pozycja 18), limit wydatków (pozycja 19). Wdrożenie stagingowe z wyłącznikiem włączonym i bez skonfigurowanej płatności wolno wcześniej; to nie jest uruchomienie.
- **Staging = środowisko `dev` w tym repozytorium.** Aplikacje Fly nazywają się `exit-interview-agent-<nazwa>-dev` (linie `app` w `flyio/*.fly.toml`). Pierwsze wdrożenie to właśnie to środowisko. Osobne środowisko produkcyjne to późniejsza decyzja; nie zmieniaj nazw aplikacji, bo `flyio.yml` tworzy je po tych nazwach.

### 1. Konto Fly i organizacja

1. Załóż albo zaloguj się na konto Fly.io, które będzie właścicielem aplikacji. Użyj konta właściciela, nie wspólnego.
2. Zainstaluj Fly CLI na swoim komputerze i zaloguj się: `fly auth login` (przykład). Zrób to samodzielnie; żadne kroki tego runbooka nie zapisują tokenu w repozytorium ani w GitHubie.
3. Sprawdź organizację, której oczekuje workflow (`FLY_ORG: personal` w `flyio.yml`), albo zmień ją w jednym miejscu.

### 2. Utworzenie aplikacji (przykłady, nie uruchomione)

Workflow `flyio.yml` tworzy brakujące aplikacje idempotentnie po wypchnięciu taga. Dopóki wdrożenie nie jest zdecydowane, przewodnik woli pipeline (FLY-IO-DEPLOYMENT §11); polecenia poniżej to udokumentowany plan awaryjny, uruchomiony raz i zapisany:

```bash
fly apps create exit-interview-agent-postgres --org personal
fly apps create exit-interview-agent-authservice-dev --org personal
fly apps create exit-interview-agent-interview-service-dev --org personal
fly apps create exit-interview-agent-web-dev --org personal
fly volumes create exit_interview_agent_pgdata --app exit-interview-agent-postgres --region fra --size 3
```

Wolumen bazy tworzy się raz, w regionie aplikacji (`fra`). Nie dodawaj drugiej maszyny bazy: dostałaby drugi, pusty wolumen (`--ha=false` w workflow).

### 3. Sekrety: co ustawić i gdzie

Sekrety ustawiasz przez `fly secrets set`, nigdy w pliku `.toml`, w commicie, PR, czacie ani dokumencie. Używaj `--stage`, żeby maszyny zrestartowały się raz, przy wdrożeniu. Nazwy sprawdzisz przez `fly secrets list` (pokazuje nazwy i skróty, nie wartości).

> **Odstępstwo od standardu, do zamknięcia przed pierwszym prawdziwym wdrożeniem.** Przewodnik ze standardów (FLY-IO-DEPLOYMENT §9, §11) mówi, że sekrety i aplikacje ustawia się z pipeline, a nie ręcznie, żeby środowiska się nie rozjeżdżały. `flyio.yml` robi to dla bazy, authservice i connection stringu usługi wywiadu. Klucz dostawcy, identyfikator workspace, model, klucze Stripe i cena **nie** są jeszcze w workflow. Do tego czasu polecenia z tej sekcji to jednorazowy ręczny bootstrap: uruchom je raz, zapisz w bramkach wydania, że to zrobiono, i przenieś wartości do workflow (jako sekrety środowiska `dev` w GitHub) osobną zmianą. Nie trzymaj ich tylko w historii powłoki.

**Usługa wywiadu** (`exit-interview-agent-interview-service-dev`):

```bash
fly secrets set --app exit-interview-agent-interview-service-dev --stage \
  "ConnectionStrings__interviewdb=<ustawiane przez pipeline z INTERVIEW_DB_PASSWORD, patrz flyio/SECRETS.md>" \
  "Ledger__ActiveKeyId=<identyfikator klucza, 1-32 znaki>" \
  "Ledger__Keys__0__Id=<ten sam identyfikator>" \
  "Ledger__Keys__0__Secret=<openssl rand -base64 32>" \
  "ANTHROPIC_API_KEY=<klucz dostawcy z dedykowanego workspace, krok 4>" \
  "Interviews__Provider=anthropic" \
  "Interviews__Model=<identyfikator modelu wybrany po przebiegu W9>" \
  "ANTHROPIC_WORKSPACE_ID=<identyfikator workspace z konsoli dostawcy>"
```

- `ANTHROPIC_API_KEY` to domyślna nazwa zmiennej z kluczem (`Interviews:ApiKeyEnv`). Jeśli ją zmienisz, ustaw też `Interviews__ApiKeyEnv` na nową nazwę.
- **Identyfikator workspace nie jest jeszcze czytany przez ten kod.** Mówi o tym [cost controls §2](web-app-cost-controls.md). Ustawiaj go dopiero, gdy kod go czyta; do tego czasu kontrolą jest limit wydatków w konsoli.
- `Interviews__Model` to wartość konfiguracyjna, nie wartość repozytorium: model wybiera się po przebiegu W9 i żaden identyfikator modelu nie trafia do repozytorium.

**Płatności** (ta sama aplikacja), dodawane w kroku 5, dopiero po sprawdzeniach w trybie testowym Stripe:

```bash
fly secrets set --app exit-interview-agent-interview-service-dev --stage \
  "Billing__Provider=stripe" \
  "Billing__StripeSecretKey=<sk_test_... z panelu Stripe w TRYBIE TESTOWYM>" \
  "Billing__StripeWebhookSecret=<whsec_... endpointu webhook w trybie testowym>" \
  "Billing__StripePriceId=<price_... jednego kredytu, tryb testowy>" \
  "Billing__PriceMinorUnits=<cena w jednostkach najmniejszych, ustalona po W9>" \
  "Billing__Currency=<kod trzyliterowy małymi literami, decyzja właściciela>" \
  "Billing__SuccessUrl=https://<twoja domena>/<strona sukcesu>" \
  "Billing__CancelUrl=https://<twoja domena>/<strona anulowania>"
```

- **Cena i waluta to Twoja decyzja** (web-app-plan §4, §9). Kod nie ma domyślnej wartości i odrzuca checkout (503), dopóki obie nie są ustawione. Nie zgaduj ich.
- W stagingu używaj klucza `sk_test_`. Przejście na klucz produkcyjny to osobny, późniejszy krok z własną pozycją w bramkach.

**Aplikacja webowa** (`exit-interview-agent-web-dev`): bez sekretów. BFF nie trzyma klucza; weryfikuje tokeny kluczami publicznymi authservice.

**Authservice** (`exit-interview-agent-authservice-dev`): sekrety z `flyio/SECRETS.md` (klucz podpisu, połączenie z bazą, sekret klienta, klucz szyfrowania). Klucz podpisu trzyma wyłącznie authservice.

### 4. Konsola dostawcy: dedykowany workspace i limit wydatków

To jedyna kontrola, która zatrzymuje rachunek ([cost controls §2](web-app-cost-controls.md)). Menu konsoli się zmieniają; sprawdzaj każdy krok z ekranem, który widzisz.

1. Utwórz **osobny workspace** dla tej aplikacji. Nie używaj workspace, z którego korzysta inna praca.
2. Utwórz klucz API w tym workspace i wstaw go tylko do `ANTHROPIC_API_KEY` (krok 3).
3. Ustaw **miesięczny limit wydatków** na workspace. Kwotę wyliczasz z zmierzonego kosztu jednej rozmowy (W9) razy liczbę rozmów, na które Cię stać. Dopóki nie ma W9, w stagingu zostaw wyłącznik włączony tylko dla testów, bez prawdziwych użytkowników.
4. Ustaw powiadomienia o limicie na adres, który czytasz.
5. Zapisz nazwę workspace i datę limitu w [RELEASE-GATE.md](../release/RELEASE-GATE.md), pozycja 19, nie klucz.

### 5. Stripe: najpierw tryb testowy, wymagany test w piaskownicy

1. Używaj wyłącznie panelu w **trybie testowym**, w stagingu. Utwórz jeden produkt z jedną ceną (jeden kredyt) i endpoint webhook `https://<twoja domena>/api/v1/webhooks/payments`. Skopiuj klucze testowe do kroku 3.
2. **Format zdarzeń webhook nie był sprawdzany na prawdziwym API Stripe** (testy używają atrapy). Przed jakąkolwiek prawdziwą płatnością wykonaj test w piaskownicy z [RELEASE-GATE.md](../release/RELEASE-GATE.md), pozycja 17: testowy zakup, podpisane testowe zdarzenie dostarczone do endpointu, kredyt dodany raz (wyślij to samo zdarzenie dwa razy; saldo nie może wzrosnąć dwa razy), podpis zły odrzucony i kwota niezgodna z ceną odrzucona.
3. Dopiero potem, i po przeglądzie prawnym, rozważ klucze produkcyjne. Ten runbook nie opisuje trybu produkcyjnego.

### 6. Authservice musi wydawać `email_verified`

Bramka zweryfikowanego e-maila (`Interviews:RequireVerifiedEmail`, produkcja: `true`) czyta claim `email_verified` z tokenu dostępu. **authservice nie umieszcza tego claimu w tokenie** (ADR-0078). Do czasu wdrożenia tej zmiany:

- każdy start rozmowy kończy się `403 email_not_verified`. To zamierzone zachowanie zamknięte domyślnie, a `/health` pokazuje bramkę.
- **Nie ustawiaj `Interviews__RequireVerifiedEmail=false` na produkcji, żeby starty działały.** Usuwa to jeden z czterech hamulców.

### 7. Domena i TLS

1. Wybierz domenę (właściciela). Wskaż ją na aplikację webową: `fly certs add <twoja domena> --app exit-interview-agent-web-dev` (przykład), potem dodaj rekord DNS, który pokaże Fly. Certyfikat wystawia Fly; aplikacje mają `force_https`.
2. Użyj tej samej domeny w `Billing__SuccessUrl`, `Billing__CancelUrl` i endpoincie webhook Stripe. Rozbieżność oznacza, że użytkownik wraca na nieistniejącą stronę albo webhook nigdy nie przychodzi.
3. Sprawdź certyfikat w przeglądarce, zanim podasz adres komukolwiek.

### 8. Wdrożenie i sprawdzenie `/health`

Wdrożenie idzie przez workflow (po tagu `v*`, który wypycha właściciel, gdy bramki na to pozwalają) albo przez `fly deploy --config flyio/<aplikacja>.fly.toml --app <aplikacja> --image <obraz>` (przykład). Potem sprawdź, w tej kolejności:

1. Web: `https://<twoja domena>/healthz` zwraca 200 (sonda z `web.fly.toml`).
2. Usługa wywiadu: `https://exit-interview-agent-interview-service-dev.fly.dev/health`. Wymienia integracje po nazwie, w tym **`interviews`** i **`billing`**, z ich stanem i powodem, gdy są zdegradowane. Stan, którego możesz się spodziewać na początku: `interviews` zdegradowane, dopóki nie ustawisz dostawcy i klucza (krok 3), `billing` zdegradowane, dopóki nie ustawisz Stripe (krok 5), i bramka e-maila odrzucająca starty, dopóki nie zrobisz kroku 6. Zdegradowana integracja jest poprawna; zdrowa przy brakującej bramce już nie.
3. Żądanie startu bez kredytu zwraca `402 payment_required`, a przy wyłączniku `503 interviews_disabled`. Oba są oczekiwaną odpowiedzią, zanim zamkniesz bramki.

### 9. Wyłącznik awaryjny

Żeby odrzucić **nowe** rozmowy od razu:

```bash
fly secrets set --app exit-interview-agent-interview-service-dev "Interviews__Enabled=false"
```

To restartuje maszyny. Nowe starty dostają `503 interviews_disabled`. Trwająca rozmowa się kończy. Wyłącznik niczego nie zwraca i nie zmienia limitu wydatków.

Żeby zatrzymać samo wydawanie pieniędzy: unieważnij lub wyłącz klucz workspace w konsoli dostawcy (trwające rozmowy wtedy się nie powiodą, a ich kredyty wracają, w sesjach, które nie są już zakończone). Potem zapisz czas i powód w bramkach wydania.

### 10. Wycofanie wersji (rollback)

- **Kod:** wylistuj wydania `fly releases --app <aplikacja>` i wdróż poprzedni obraz: `fly deploy --app <aplikacja> --image ghcr.io/konradcinkusz/exit-interview-agent-<nazwa>:<poprzedni tag>` (przykład). Każdy obraz ma tag wydania, więc poprzedni jest nadal w GHCR.
- **Baza:** migracje schematu **nie** są cofane przez rollback kodu. Jeśli problemem jest migracja, przywróć wolumen PostgreSQL z kopii zapasowej wykonanej przed wdrożeniem; zrób ją przed krokiem 8, nie po nim.
- **Sekrety:** `fly secrets unset` usuwa jedną nazwę; stara wartość przepada, więc nowe wartości trzymaj w menedżerze haseł, nie w pliku w repozytorium.

### 11. Po restarcie

Sesje rozmów żyją **w pamięci** (web-app-plan §3, ADR-0076). Wdrożenie, zmiana skali, wymiana maszyny albo awaria kończy każdą otwartą sesję:

- kolejne żądanie użytkownika dostaje `410 gone`, a transkrypt przepada (nie ma kopii do odzyskania, z założenia);
- **znana luka:** kredyt otwartej sesji **nie** wraca automatycznie przy restarcie. Ledger zwraca go dopiero w pracy oznaczonej jako W11. Do czasu W11 rozstrzygaj każdy przypadek ręcznie: sprawdź ledger pod kątem kredytów zużytych bez zakończonego wyniku i zwracaj pieniądze przez dostawcę płatności tylko tam, gdzie sesję zgubiła usługa. Nie odtwarzaj czyjejś rozmowy z żadnego logu: żaden nie zawiera tekstu rozmowy.
- żeby uniknąć luki: nie wdrażaj ani nie restartuj, gdy mogą trwać rozmowy. Żadna metryka z listy (cost controls §4) nie podaje liczby otwartych sesji, więc wybierz spokojną porę na podstawie zużycia w konsoli dostawcy i liczby startów, i zapisz to w uwagach bramek.
