# Web interview: cost controls runbook

Status: written for the hosted interview (web-app-plan §2, ADR-0078). **Nothing here is deployed.** The steps describe what the owner does when the app runs. Where a step depends on a provider console whose screens change, it says so: check the screen you see against this text.

Languages: **English** first, then **Polski** (below).

---

## English

### 1. The four brakes

| Brake | Where it is set | What it does | Answer to a request |
|---|---|---|---|
| Emergency switch | `Interviews:Enabled` (environment variable `Interviews__Enabled`) | Refuses **new** interviews. A running interview can finish. | `503 interviews_disabled` |
| Per-account and per-address limit | `Interviews:RateLimits:*` | Starts: 5 per account and 20 per address per hour. Replies: 30 per account and 60 per address per minute. | `429 rate_limited`, `Retry-After` header |
| Verified email | `Interviews:RequireVerifiedEmail` (production: `true`) | Refuses starts for an account whose token does not say its email is verified. | `403 email_not_verified` |
| Daily start cap | `Interviews:MaxStartsPerDay` (default 20) | Counts admitted starts per UTC day, across all accounts. At the cap, new starts wait until midnight UTC. `0` refuses every start. | `503 interviews_disabled`, `Retry-After` up to midnight UTC |

The numbers are the defaults in `appsettings.json` and are placeholders until the W9 real-model run measures the cost of one interview. Change them in configuration; do not edit code.

**Known state of the email gate.** The gate needs authservice to put `email_verified` in the access token. It does not do that today (ADR-0078). While `RequireVerifiedEmail` is `true`, every start is refused. That is the fail-closed setting, and `/health` shows it. Do not switch the gate off in production to make starts work; the authservice change is the fix.

The switch is read on every start. The rate limits and the gate are read when the process starts, so changing them needs a restart. The daily count is in memory and resets on a restart.

### 2. Set a spend limit in the provider console (the real money brake)

The application's brakes count requests. The provider's spend limit is the only control that stops the bill, so set it as well.

1. Create a **separate workspace** for this app in the provider console. Do not reuse a workspace that other work uses; the limit applies to everything in the workspace.
2. In that workspace, create the API key the service will use. Store it only as the deployment's secret (for example a Fly secret). It must never be committed, pasted into a chat or written in a document.
3. Set a **monthly spend limit** on the workspace. Choose the amount from the measured cost of one interview (W9) times the number of interviews you can afford in a month. Until W9 exists, do not launch a paid interview.
4. Set the limit's notification to an address you read.
5. Record the workspace name and the limit's date in the release gate (not in this file, and not the key).

The app reads a workspace identifier from `ANTHROPIC_WORKSPACE_ID` in the plan. **This code does not read that variable yet**; the spend limit is what matters, and it is set in the console. Check the console's screens against these steps: the menu names change.

### 3. What the switch does and does not do

- `Interviews:Enabled=false` stops new interviews at once. It does not refund anything and does not stop a running interview.
- It does not change the spend limit. The bill can still grow from interviews already running.
- To stop everything at once, combine it with a provider-side step: revoke or disable the workspace's key in the provider console. The running interviews then fail and their credits come back.

### 4. How to read the metrics

The metrics carry no labels: they are counts and sums, never per account, session or address. Names are as the service defines them; a Prometheus-style exporter may add a `_total` suffix to counters.

| Metric | Kind | Read it as |
|---|---|---|
| `interviews_started` | counter | Starts that were admitted (201). |
| `interviews_completed` | counter | Interviews that ended with a record. |
| `interviews_failed` | counter | Interviews that ended as a service fault (their credit comes back). |
| `interviews_withdrawn` | counter | Interviews ended by the person (deleted, consent refused, stopped). |
| `rate_limited` | counter | Requests refused by the per-account or per-address limit. |
| `rejected_email_unverified` | counter | Starts refused by the verified-email gate. |
| `tokens_estimated` | histogram | Estimated tokens per interview. |
| `model_calls` | histogram | Model calls per interview. |

Useful ratios (compute them in the query engine, from `_sum` and `_count` per METRICS-EXPOSITION §4):

- **Average tokens per interview** = sum of `tokens_estimated` / count of `tokens_estimated`.
- **Average model calls per interview** = sum of `model_calls` / count of `model_calls`.
- **Failure share** = `interviews_failed` / `interviews_started` over the same window. A rising share is a provider or code problem, not a people problem.
- **Withdrawal share** = `interviews_withdrawn` / `interviews_started`. This is an experience signal; do not use it to judge a person.

Each number comes with its sample size (the count). Early numbers from a handful of interviews mean nothing yet: METRIC-ETHICS §3.

### 5. A sudden rise in cost

Work through these in order. Stop as soon as the cost is under control.

1. **Look at the provider console's usage** for the workspace: is the spend coming from this app's key? If the spend is from another key, rotate the key that is not yours.
2. **Read the metrics for the last hour.** Compare `interviews_started` with the hour before.
   - A rise in `rate_limited` with a steady `interviews_started`: someone is hammering the endpoint. The limits hold it; check the provider's usage, then consider lowering `RateLimits:*` and a restart.
   - A rise in `rejected_email_unverified`: someone is trying without a verified account. The gate holds it.
   - A rise in `interviews_started` with normal `tokens_estimated` per interview: real demand, or many accounts. Lower `MaxStartsPerDay` to the number you can afford.
   - A rise in `tokens_estimated` per interview with normal starts: the interviews run longer than planned. Lower the per-interview budget in the protocol (web-app-plan §2) and check the latest changes.
3. **Stop new interviews:** set `Interviews__Enabled=false` in the environment and restart the service. `/health` then shows the switch as off.
4. **Cap the day:** if you need the service on at a lower cost, set `Interviews__MaxStartsPerDay` to the amount you can afford (or `0`) and restart.
5. **Lower the spend limit** in the provider console if the monthly total is near the limit.
6. **Rotate the key** if the spend cannot be explained by the metrics. The old key stops working; the new key goes into the deployment's secret, never into a file.
7. **Write down what happened** in the release gate or an incident note: the time, the metric that showed it, and the step that stopped it. Do not copy any interview text or personal data into the note.

### 6. What the service never stores or logs

No interview text, no account identifier, no address and no key appears in a metric, a log line or this runbook. If you find one in a log, treat it as a bug and report it; do not paste the line anywhere.

### 7. Sessions lost at a restart (credits returned by the startup sweep)

Sessions live in memory, so a restart ends every open interview. Each one's credit was spent when it started; the startup sweep returns it. Each start of the service runs a sweep that settles every credit with no settlement row, that started more than 95 minutes before it (a 30-minute idle window, a 5-minute margin, and up to an hour of rounding, because a start is recorded to its hour), and that this process does not hold. Each is returned with the reason `RefundLostSession` and its settlement is written as `lost`. Count them with `interviews_lost_refunded` (no labels). A restart does not return a credit of a session started within that window: the first start after the window does.

What to do: after a restart, read the counter. A high number means many interviews were open when the process stopped (a deploy at a busy hour, or a crash); compare it with `interviews_started` and `interviews_failed` for the same period before you act. Do not edit the ledger by hand: the sweep is idempotent, and a manual row would break the one-settlement-per-session rule. To check one session, the settlement row is the only record; its session id is never in a log line.

---

## Polski

### 1. Cztery hamulce

| Hamulec | Gdzie ustawiony | Co robi | Odpowiedź na żądanie |
|---|---|---|---|
| Wyłącznik awaryjny | `Interviews:Enabled` (zmienna środowiskowa `Interviews__Enabled`) | Odrzuca **nowe** rozmowy. Trwająca rozmowa może się dokończyć. | `503 interviews_disabled` |
| Limit na konto i na adres | `Interviews:RateLimits:*` | Starty: 5 na konto i 20 na adres na godzinę. Odpowiedzi: 30 na konto i 60 na adres na minutę. | `429 rate_limited`, nagłówek `Retry-After` |
| Zweryfikowany e-mail | `Interviews:RequireVerifiedEmail` (produkcja: `true`) | Odrzuca starty dla konta, którego token nie potwierdza zweryfikowanego e-maila. | `403 email_not_verified` |
| Dzienny limit startów | `Interviews:MaxStartsPerDay` (domyślnie 20) | Liczy przyjęte starty w dniu UTC, dla wszystkich kont. Po osiągnięciu limitu nowe starty czekają do północy UTC. `0` odrzuca każdy start. | `503 interviews_disabled`, `Retry-After` do północy UTC |

Liczby są wartościami domyślnymi z `appsettings.json` i pozostają roboczymi, dopóki przebieg W9 na prawdziwym modelu nie zmierzy kosztu jednej rozmowy. Zmieniaj je w konfiguracji, nie w kodzie.

**Stan bramki e-mail.** Bramka wymaga, by authservice umieszczał `email_verified` w tokenie dostępu. Dziś tego nie robi (ADR-0078). Dopóki `RequireVerifiedEmail` ma wartość `true`, każdy start jest odrzucany. To ustawienie zamknięte domyślnie, a `/health` je pokazuje. Nie wyłączaj bramki na produkcji, żeby starty działały; poprawką jest zmiana w authservice.

Wyłącznik jest czytany przy każdym starcie. Limity i bramka są czytane przy starcie procesu, więc zmiana wymaga restartu. Dzienny licznik jest w pamięci i zeruje się przy restarcie.

### 2. Ustaw limit wydatków w konsoli dostawcy (prawdziwy hamulec pieniędzy)

Hamulce aplikacji liczą żądania. Jedynym elementem, który zatrzymuje rachunek, jest limit wydatków u dostawcy, więc ustaw go także.

1. Utwórz **osobny workspace** dla tej aplikacji w konsoli dostawcy. Nie używaj workspace'u, w którym są inne prace; limit dotyczy wszystkiego w nim.
2. W tym workspace'ie utwórz klucz API, którego użyje usługa. Trzymaj go wyłącznie jako sekret wdrożenia (np. sekret Fly). Nigdy nie commituj go, nie wklejaj do czatu ani do dokumentu.
3. Ustaw **miesięczny limit wydatków** na workspace. Kwotę wybierz z kosztu jednej rozmowy zmierzonego w W9, pomnożonego przez liczbę rozmów, na które cię stać w miesiącu. Dopóki W9 nie istnieje, nie uruchamiaj płatnej rozmowy.
4. Ustaw powiadomienie o limicie na adres, który czytasz.
5. Zapisz nazwę workspace'u i datę ustawienia limitu w bramce wydania (nie w tym pliku, i nigdy nie zapisuj klucza).

Plan wspomina o identyfikatorze workspace'u w `ANTHROPIC_WORKSPACE_ID`. **Ten kod tej zmiennej jeszcze nie czyta**; liczy się limit wydatków, ustawiany w konsoli. Sprawdź ekrany konsoli z tymi krokami: nazwy menu się zmieniają.

### 3. Co robi wyłącznik, a czego nie robi

- `Interviews:Enabled=false` zatrzymuje nowe rozmowy od razu. Nic nie zwraca i nie zatrzymuje trwającej rozmowy.
- Nie zmienia limitu wydatków. Rachunek może nadal rosnąć z rozmów, które już trwają.
- By zatrzymać wszystko naraz, połącz go z krokiem po stronie dostawcy: unieważnij lub wyłącz klucz workspace'u w konsoli dostawcy. Trwające rozmowy wtedy się nie powiodą, a ich kredyty wrócą.

### 4. Jak czytać metryki

Metryki nie mają etykiet: to liczby i sumy, nigdy na konto, sesję czy adres. Nazwy są takie, jak je definiuje usługa; eksporter w stylu Prometheus może dodać przyrostek `_total` do liczników.

| Metryka | Rodzaj | Jak czytać |
|---|---|---|
| `interviews_started` | licznik | Przyjęte starty (201). |
| `interviews_completed` | licznik | Rozmowy zakończone rekordem. |
| `interviews_failed` | licznik | Rozmowy zakończone błędem usługi (kredyt wraca). |
| `interviews_withdrawn` | licznik | Rozmowy zakończone przez osobę (usunięte, zgoda odrzucona, zatrzymane). |
| `rate_limited` | licznik | Żądania odrzucone przez limit na konto lub adres. |
| `rejected_email_unverified` | licznik | Starty odrzucone przez bramkę e-mail. |
| `tokens_estimated` | histogram | Szacowane tokeny na rozmowę. |
| `model_calls` | histogram | Wywołania modelu na rozmowę. |

Przydatne proporcje (licz je w silniku zapytań z `_sum` i `_count`, zgodnie z METRICS-EXPOSITION §4):

- **Średnie tokeny na rozmowę** = suma `tokens_estimated` / liczba `tokens_estimated`.
- **Średnie wywołania modelu na rozmowę** = suma `model_calls` / liczba `model_calls`.
- **Udział błędów** = `interviews_failed` / `interviews_started` w tym samym oknie. Rosnący udział to problem dostawcy lub kodu, nie ludzi.
- **Udział wycofań** = `interviews_withdrawn` / `interviews_started`. To sygnał o doświadczeniu; nie oceniaj nim człowieka.

Każda liczba ma swoją wielkość próby (liczbę). Wczesne liczby z garstki rozmów nic nie znaczą: METRIC-ETHICS §3.

### 5. Nagły wzrost kosztu

Przejdź po kolei. Zatrzymaj się, gdy koszt jest pod kontrolą.

1. **Sprawdź zużycie w konsoli dostawcy** dla workspace'u: czy wydatki pochodzą z klucza tej aplikacji? Jeśli z innego klucza, unieważnij klucz, którego nie znasz.
2. **Odczytaj metryki z ostatniej godziny.** Porównaj `interviews_started` z godziną wcześniej.
   - Wzrost `rate_limited` przy stałym `interviews_started`: ktoś uderza w endpoint. Limity to trzymają; sprawdź zużycie u dostawcy, potem rozważ obniżenie `RateLimits:*` i restart.
   - Wzrost `rejected_email_unverified`: ktoś próbuje bez zweryfikowanego konta. Bramka to trzyma.
   - Wzrost `interviews_started` przy normalnych `tokens_estimated` na rozmowę: prawdziwy popyt albo wiele kont. Obniż `MaxStartsPerDay` do tego, na co cię stać.
   - Wzrost `tokens_estimated` na rozmowę przy normalnych startach: rozmowy trwają dłużej niż planowano. Obniż budżet na rozmowę (plan webowy §2) i sprawdź ostatnie zmiany.
3. **Zatrzymaj nowe rozmowy:** ustaw `Interviews__Enabled=false` w środowisku i zrestartuj usługę. `/health` pokaże wtedy wyłącznik jako wyłączony.
4. **Ogranicz dzień:** jeśli usługa ma działać taniej, ustaw `Interviews__MaxStartsPerDay` na kwotę, na którą cię stać (lub `0`) i zrestartuj.
5. **Obniż limit wydatków** w konsoli dostawcy, jeśli suma miesięczna jest blisko limitu.
6. **Wymień klucz**, jeśli metryki nie tłumaczą wydatków. Stary klucz przestaje działać; nowy idzie do sekretu wdrożenia, nigdy do pliku.
7. **Zapisz, co się stało** w bramce wydania lub notatce o incydencie: czas, metryka, która to pokazała, i krok, który zatrzymał. Nie kopiuj do notatki żadnego tekstu rozmowy ani danych osobowych.

### 6. Czego usługa nigdy nie zapisuje ani nie loguje

Żaden tekst rozmowy, identyfikator konta, adres ani klucz nie pojawia się w metryce, w linii logu ani w tym runbooku. Jeśli znajdziesz któryś w logu, traktuj to jako błąd i zgłoś; nie wklejaj tej linii nigdzie.

### 7. Sesje przerwane restartem (kredyty zwracane przez sweep przy starcie)

Sesje żyją w pamięci, więc restart kończy wszystkie otwarte rozmowy. Kredyt każdej z nich został zużyty przy starcie; sweep przy starcie usługi go zwraca. Przy każdym starcie usługi sweep rozlicza wszystkie kredyty bez wiersza rozliczenia, rozpoczęte więcej niż 95 minut przed tym startem (okno bezczynności 30 minut, margines 5 minut i do godziny zaokrąglenia, bo start zapisywany jest z dokładnością do godziny), których proces nie trzyma. Każdy zwracany jest z powodem `RefundLostSession`, a jego rozliczenie zapisywane jest jako `lost`. Liczbę mierz metryką `interviews_lost_refunded` (bez etykiet). Kredyt sesji rozpoczętej w tym oknie nie wraca przy tym restarcie, tylko przy pierwszym starcie po jego upływie.

Co zrobić: po restarcie odczytaj licznik. Duża liczba oznacza, że w chwili zatrzymania było otwartych wiele rozmów (wdrożenie w godzinie szczytu albo awaria); porównaj ją z `interviews_started` i `interviews_failed` z tego samego okresu, zanim cokolwiek zrobisz. Nie edytuj ręcznie ledgera: sweep jest idempotentny, a ręczny wiersz złamałby regułę jednego rozliczenia na sesję. Rozliczenie sesji to jedyny ślad; jej identyfikator nigdy nie trafia do linii logu.
