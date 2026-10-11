import type { Messages } from "./en";

// The portal catalog in Polish, typed as `Messages`: a missing or renamed key is a compile error, not a blank on the page. It is a
// builder's draft until a native speaker has reviewed it (docs/ux/UI-UX.md, the same gate as the interview copy). Forms that would
// need a grammatical gender use both ("pracowałeś/pracowałaś") or an impersonal construction.

export const pl: Messages = {
  site: {
    name: "Exit Interview Agent",
    description: "Przeprowadź rozmowę wyjściową z asystentem AI, przeczytaj zapis, który powstanie, i usuń wszystko, kiedy skończysz.",
    devBuild: "Wersja deweloperska: tylko symulowane rozmowy. Nic tu nie jest wdrożone i nie wolno wpisywać prawdziwych danych osobowych.",
    skipToContent: "Przejdź do treści",
    loading: "Ładowanie…",
  },
  nav: {
    label: "Menu główne",
    home: "Strona główna",
    interview: "Rozmowa",
    account: "Konto",
    signIn: "Zaloguj się",
    signOut: "Wyloguj się",
  },
  language: {
    label: "Język",
    switchTo: "English",
    switchToLang: "en",
    switchHint: "Switch to English",
    englishOnly: "Ta strona jest dostępna tylko po angielsku.",
  },
  footer: {
    label: "Więcej",
    privacy: "Prywatność",
    about: "O projekcie",
    developers: "Dla programistów",
  },
  landing: {
    title: "Przeprowadź rozmowę wyjściową z AI",
    lead:
      "Odchodzisz z pracy i chcesz powiedzieć, jak naprawdę było. Asystent AI pyta Cię o sześć tematów, po kolei, po polsku " +
      "albo po angielsku. Na końcu dostajesz krótki zapis tego, co powiedziałeś/powiedziałaś, i szkice tekstów, które możesz " +
      "skopiować, zmienić albo wyrzucić.",
    start: "Rozpocznij rozmowę",
    startNote: "Najpierw się zalogujesz albo założysz konto, jeśli jeszcze go nie masz.",
    keptTitle: "Co jest przechowywane, a co nie",
    kept:
      "Twoje konto to adres e-mail, hasło i zaakceptowane warunki. Rozmowa trafia do firmy Anthropic, która dostarcza model AI, " +
      "żeby mógł odpowiadać; ta strona trzyma ją tylko w pamięci, dopóki rozmowa trwa. Po zakończeniu zapis i szkice są " +
      "dostępne przez 30 minut, potem są usuwane. Możesz usunąć wszystko wcześniej.",
    keptLink: "Zasady prywatności",
    stepsTitle: "Jak to działa",
    steps: [
      "Zaloguj się albo załóż konto, podając adres e-mail.",
      "Wybierz język i czas zatrudnienia, a potem przeczytaj, co dzieje się z Twoimi słowami.",
      "Porozmawiaj z asystentem AI w czacie. Możesz przerwać w każdej chwili.",
      "Przeczytaj zapis i szkice tekstów, skopiuj albo pobierz to, czego potrzebujesz, a resztę usuń.",
    ],
    notTitle: "Czym to nie jest",
    not: [
      "To nie jest portal z opiniami: ta strona niczego, co powiesz, nie publikuje.",
      "To nie jest porada prawna: szkice są szkicami, a nie faktami.",
      "To nie jest weryfikacja zatrudnienia: nikt nie sprawdza, gdzie pracowałeś/pracowałaś.",
    ],
    aboutLink: "O tym projekcie open source",
  },
  about: {
    title: "O projekcie",
    lead:
      "Exit Interview Agent to projekt open source. Poza rozmową na tej stronie tę samą uporządkowaną rozmowę można " +
      "przeprowadzić we własnym kliencie AI albo w narzędziu wiersza poleceń, z własnym modelem. Wtedy ta strona nie uruchamia " +
      "żadnego modelu i nie widzi rozmowy; dostaje tylko końcowy zapis, jeśli zdecydujesz się go przesłać.",
    pathsTitle: "Jeśli korzystasz z własnego klienta AI albo wiersza poleceń",
    storedTitle: "Co jest przechowywane",
    stored: [
      "Twoje konto (adres e-mail i dane logowania) oraz udzielone zgody, przechowywane przez usługę tożsamości.",
      "Przesłany zapis: oceny dla każdego tematu, krótkie cytaty, pracodawca i ogólne przedziały. Nie zawiera konta, imienia, nazwiska ani adresu e-mail.",
      "Jednokierunkowy znacznik dla każdego pracodawcy, który nie pozwala temu samemu kontu przesłać zapisu dwa razy. Nie zawiera treści i jest usuwany automatycznie.",
      "Dopóki bilet nie zostanie użyty: jego skrót, konto, dla którego powstał, i data ważności.",
    ],
    notStoredTitle: "Co nie jest przechowywane",
    notStored: [
      "Transkrypcja rozmowy. Zostaje w Twoim kliencie AI albo na Twoim komputerze.",
      "Powiązanie między Twoim kontem a przesłanym zapisem. Nikt, także operator, nie może wypisać Twoich zapisów.",
      "Twój kod potwierdzenia w czytelnej postaci ani informacja, z którego konta pochodzi.",
      "Treść rozmów, imiona, nazwiska ani adresy e-mail w logach.",
    ],
    recordsNote:
      "Przesłany zapis jest traktowany jako dane osobowe. Cytaty mogą być rozpoznawalne dla osób, które Cię znają, dlatego ta " +
      "usługa nigdy nie nazywa zapisów anonimowymi.",
    nonGoalsTitle: "Czym to nie jest",
    nonGoals: [
      "To nie jest publiczny portal z opiniami: pojedyncze zapisy nigdy nie są publikowane, tylko zestawienia powyżej minimalnej liczby zapisów.",
      "To nie jest źródło zweryfikowanego zatrudnienia: weryfikacja nie istnieje.",
      "W ścieżce z klientem AI i wierszem poleceń to nie jest hosting modelu: model zapewniasz Ty, przez swój klient AI, własny klucz API albo model lokalny.",
    ],
    sourceTitle: "Kod źródłowy",
    sourceLink: "Repozytorium projektu na GitHubie",
    sourceHref: "https://github.com/konradcinkusz/exit-interview-agent",
    developersLink: "Strony dla programistów",
  },
  developers: {
    title: "Dla programistów",
    lead:
      "Te strony są dla osób, które prowadzą rozmowę we własnym kliencie AI albo w wierszu poleceń, oraz dla zestawień o " +
      "pracodawcach budowanych z przesłanych zapisów. Do rozmowy na tej stronie żadna z nich nie jest potrzebna.",
    links: [
      { href: "/connect", label: "Connect your AI client", what: "Podłącz tę usługę do Claude jako konektor, żeby rozmowa odbywała się w Twojej własnej rozmowie z Claude.", gated: false },
      { href: "/cli", label: "Use the command line", what: "Utwórz jednorazowy bilet, żeby narzędzie wiersza poleceń mogło przesłać zapis w Twoim imieniu.", gated: true },
      { href: "/delete-submission", label: "Delete a submission", what: "Usuń zapis przesłany z klienta AI albo z wiersza poleceń, podając jego kod potwierdzenia.", gated: false },
      { href: "/signals", label: "Employer signals", what: "Zestawienia o pracodawcach, zawsze z niepewnością. Nigdy ranking.", gated: true },
    ],
    englishOnly: "Te strony są dostępne tylko po angielsku.",
  },
  devHints: {
    title: "Lokalne środowisko deweloperskie",
    intro: "Widoczne tylko dlatego, że ta aplikacja działa z DEV_HINTS=1 (ustawia to lokalny AppHost). Nigdy nie pojawia się we wdrożeniu.",
    email: "Testowy e-mail",
    password: "Testowe hasło",
    seed: "Jeśli logowanie się nie uda, najpierw uruchom „Seed test user (with credits)” z menu … zasobu postgres, authdb albo interviewdb w panelu Aspire. Tworzy ono to konto z kredytami na rozmowy.",
  },
  login: {
    title: "Zaloguj się",
    introInterview: "Zaloguj się, aby rozpocząć rozmowę wyjściową.",
    intro: "Zaloguj się na swoje konto.",
    email: "E-mail",
    password: "Hasło",
    submit: "Zaloguj się",
    noAccount: "Nie masz jeszcze konta?",
    register: "Załóż je",
    twoFactorTitle: "Logowanie dwuskładnikowe",
    twoFactorIntro: "Wpisz 6-cyfrowy kod z aplikacji uwierzytelniającej.",
    code: "Kod z aplikacji",
    useRecovery: "Użyj kodu odzyskiwania",
    useCode: "Użyj kodu z aplikacji",
    recoveryCode: "Kod odzyskiwania",
    recoveryHint: "Kod odzyskiwania działa tylko raz.",
    twoFactorSubmit: "Sprawdź i zaloguj",
    twoFactorBack: "Zacznij od nowa",
    errors: {
      invalid_credentials: "Nieprawidłowy e-mail lub hasło.",
      email_not_verified: "Najpierw potwierdź adres e-mail. Użyj linku z wiadomości, którą wysłaliśmy, albo poproś o nowy.",
      rate_limited: "Za dużo prób. Spróbuj ponownie za minutę.",
      identity_not_configured: "Logowanie nie jest skonfigurowane w tym środowisku.",
      invalid_code: "Ten kod jest nieprawidłowy. Sprawdź kod i zegar w urządzeniu, potem spróbuj ponownie.",
      challenge_expired: "To logowanie wygasło. Zacznij od nowa, podając e-mail i hasło.",
      locked: "Konto jest tymczasowo zablokowane po zbyt wielu nieudanych próbach. Spróbuj później.",
      generic: "Logowanie nie powiodło się. Spróbuj ponownie.",
    },
  },
  register: {
    title: "Załóż konto",
    intro: "Konto służy tylko do logowania. Nie jest powiązane z żadnym pracodawcą ani z tym, co przesyłasz.",
    email: "E-mail",
    password: "Hasło",
    passwordHint: "Co najmniej 8 znaków.",
    terms: "Akceptuję Regulamin",
    privacy: "Akceptuję Politykę prywatności",
    version: "Wersja",
    submit: "Załóż konto",
    haveAccount: "Masz już konto?",
    signIn: "Zaloguj się",
    created: "Konto zostało założone. Możesz się teraz zalogować.",
    createdVerify: "Konto zostało założone. Sprawdź pocztę: kliknij link, żeby potwierdzić adres, a potem się zaloguj.",
    errors: {
      invalid: "Nie udało się założyć konta. Sprawdź adres e-mail i wybierz dłuższe albo inne hasło.",
      both_required: "Aby założyć konto, zaakceptuj oba dokumenty.",
      rate_limited: "Za dużo prób. Spróbuj ponownie za minutę.",
      identity_not_configured: "Rejestracja nie jest skonfigurowana w tym środowisku.",
      generic: "Nie udało się założyć konta. Spróbuj ponownie.",
    },
  },
  verifyEmail: {
    title: "Potwierdź adres e-mail",
    working: "Sprawdzam…",
    done: "Adres e-mail jest potwierdzony. Możesz się teraz zalogować.",
    failed: "Ten link jest nieprawidłowy albo wygasł. Poproś poniżej o nowy.",
    missing: "Otwórz link z wiadomości potwierdzającej albo poproś poniżej o nowy.",
    resendTitle: "Wyślij nowy link",
    email: "E-mail",
    resend: "Wyślij nowy link",
    resent: "Jeśli ten adres wymaga potwierdzenia, wysłaliśmy nowy link.",
    rateLimited: "Za dużo prób. Spróbuj ponownie za minutę.",
    signIn: "Przejdź do logowania",
    generic: "Coś poszło nie tak. Spróbuj ponownie.",
  },
  consent: {
    title: "Zanim przejdziesz dalej",
    intro:
      "Aby korzystać z usługi, zaakceptuj aktualne wersje tych dokumentów. Ta strona pojawia się po pierwszym zalogowaniu " +
      "i ponownie wtedy, gdy któryś z nich się zmieni.",
    terms: "Akceptuję Regulamin",
    privacy: "Akceptuję Politykę prywatności",
    version: "Wersja",
    termsSummary: "Regulamin korzystania z tej usługi.",
    privacySummary:
      "Polityka prywatności: ten portal przechowuje Twoje konto i zgody, przesłane zapisy nie są powiązane z kontem, a w logach " +
      "nie zapisuje się treści rozmów, imion, nazwisk ani adresów e-mail.",
    accept: "Akceptuję i przechodzę dalej",
    decline: "Odrzucam i wylogowuję się",
    loadError: "Nie udało się sprawdzić stanu zgód. Spróbuj ponownie.",
    saveError: "Nie udało się zapisać zgody. Spróbuj ponownie.",
  },
  account: {
    title: "Moje konto",
    signedIn: "Jesteś zalogowany/zalogowana.",
    accountId: "Identyfikator konta",
    loadError: "Nie udało się wczytać konta. Spróbuj później.",
    signOut: "Wyloguj się",
    interviewTitle: "Twoja rozmowa",
    interviewText: "Rozpocznij, kontynuuj albo usuń rozmowę wyjściową na stronie rozmowy.",
    interviewLink: "Przejdź do rozmowy",
    exportTitle: "Pobierz swoje dane",
    exportIntro: "Pobierz to, co usługa tożsamości przechowuje o Twoim koncie: profil, zgody, metody logowania i sesje.",
    exportNot:
      "Przesłanych zapisów tu nie ma. Nie są powiązane z Twoim kontem, więc ani usługa tożsamości, ani ta strona nie potrafią ich znaleźć.",
    exportButton: "Pobierz moje dane (JSON)",
    deleteTitle: "Usuń konto",
    deleteLabel: "Usuń konto",
    password: "Hasło",
    passwordHint: "(niepotrzebne, jeśli logujesz się przez zewnętrznego dostawcę)",
    confirm: "Wpisz DELETE, aby potwierdzić",
    deleteSubmit: "Usuń moje konto",
    deletion: {
      headline: "Co robi usunięcie konta, a czego nie robi",
      removes:
        "Usuwa Twój login. Konto zostaje zamknięte od razu, wszystkie sesje się kończą, a usługa tożsamości usuwa samo konto " +
        "po upływie okresu przechowywania.",
      notSubmissions:
        "Nie usuwa niczego, co przesłałeś/przesłałaś. Przesłane zapisy celowo nie są powiązane z kontem: nikt, także operator " +
        "usługi, nie wie, które zapisy pochodzą z którego konta, więc zamknięcie konta nie może ich usunąć.",
      receipt:
        "Aby usunąć przesłany zapis, użyj jego kodu potwierdzenia. Kod, który dostałeś/dostałaś przy przesłaniu, to jedyny " +
        "sposób na usunięcie zapisu, więc go chroń: zgubionego kodu nie da się odzyskać ani zastąpić.",
      ledger:
        "Usługa przechowuje też jednokierunkowy znacznik dla każdego pracodawcy, który nie pozwala temu samemu kontu przesłać " +
        "zapisu dwa razy. Nie zawiera treści ani powiązania z zapisem i jest usuwany automatycznie po upływie okresu przechowywania.",
    },
    errors: {
      password_required: "Wpisz hasło, aby potwierdzić.",
      invalid_password: "To hasło jest nieprawidłowe.",
      confirmation_required: "Wpisz dokładnie DELETE, aby potwierdzić.",
      unauthenticated: "Sesja się skończyła. Zaloguj się ponownie.",
      identity_unavailable: "Nie udało się połączyć z usługą tożsamości. Nic nie zostało usunięte; spróbuj ponownie.",
      generic: "Nie udało się usunąć konta. Spróbuj ponownie.",
    },
  },
  accountDeleted: {
    title: "Twoje konto jest zamknięte",
    back: "Wróć na stronę główną",
    deleteSubmission: "Usuń przesłany zapis kodem potwierdzenia (strona po angielsku)",
  },
  privacy: {
    title: "Prywatność w skrócie",
    lead: "Krótka wersja zasad prywatności. Pełny opis, z jego ograniczeniami, jest podlinkowany na dole i to on jest rozstrzygający.",
    interviewTitle: "Rozmowa na tej stronie",
    interview: [
      "Rozmówcą jest AI i mówi o tym na początku rozmowy.",
      "Wszystko, co wpiszesz, trafia do dostawcy modelu AI, firmy Anthropic, który pisze każdą odpowiedź. Obowiązują przy tym warunki i zasady przetwarzania danych Anthropic.",
      "Podczas rozmowy jej treść jest trzymana w pamięci serwera. Po zakończeniu rozmowa jest odrzucana; transkrypcja nie jest zapisywana.",
      "Po zakończeniu rozmowy zapis i szkice tekstów są dostępne przez 30 minut, potem są usuwane. Możesz usunąć je wcześniej.",
      "Ta strona niczego nie zapisuje w pamięci przeglądarki: odświeżenie strony czyści rozmowę i wynik.",
    ],
    factsTitle: "Zapisy przesłane z klienta AI albo z wiersza poleceń",
    facts: [
      "Transkrypcja rozmowy zostaje po Twojej stronie: w Twoim kliencie AI albo na Twoim komputerze. Usługa dostaje tylko końcowy zapis.",
      "Zapis nie zawiera konta, imienia, nazwiska ani adresu e-mail, ale nadal jest danymi osobowymi: cytaty i pracodawca mogą wskazać osobę w małym zespole. Nigdy nie nazywamy go anonimowym.",
      "Twoje konto nie jest powiązane z przesłanymi zapisami. Nie ma listy tego, co przesłałeś/przesłałaś, i nikt nie może jej utworzyć.",
      "Żeby to samo konto nie przesłało dwa razy zapisu o tym samym pracodawcy, usługa przechowuje jednokierunkowy znacznik dla konta i pracodawcy. Nie zawiera treści i jest usuwany po okresie przechowywania.",
      "Kod potwierdzenia usuwa zapis, do którego należy. Jest pokazywany raz i nie da się go odzyskać. Odpowiedź na usunięcie nigdy nie zdradza, czy zapis istniał.",
      "Zapisy są usuwane automatycznie po maksymalnym czasie, który ustala operator.",
      "Logi, ślady i metryki nie zawierają treści rozmów, imion, nazwisk ani adresów e-mail.",
      "Wyniki są pokazywane tylko jako zestawienia powyżej minimalnej liczby zapisów, z ich niepewnością. Pojedyncze zapisy nigdy nie są publikowane.",
    ],
    limitsTitle: "Czego nie możemy obiecać",
    limits: [
      "Operator, który ma bazę danych, tajny klucz do znaczników i dostęp do ruchu na żywo, mógłby z wysiłkiem ustalić, które konto przesłało zapis o którym pracodawcy. To ograniczenie projektu, mówimy o nim otwarcie.",
      "Klient AI albo dostawca, z którego korzystasz, widzi Twoją rozmowę na warunkach, które masz z nim uzgodnione.",
      "Krótkie zapisy z małych zespołów mogą być rozpoznane przez osoby, które znają zespół.",
    ],
    cookiesTitle: "Pliki cookie",
    cookies:
      "Ta strona ustawia tylko niezbędne pliki cookie: sesję logowania (HttpOnly, niedostępną dla skryptów strony) oraz, jeśli go zmienisz, wybrany język.",
    linksTitle: "Pełne dokumenty",
    links: [
      { label: "Projekt prywatności (Privacy design)", href: "https://github.com/konradcinkusz/exit-interview-agent/blob/main/docs/privacy/DESIGN.md" },
      { label: "Model zagrożeń (Threat model)", href: "https://github.com/konradcinkusz/exit-interview-agent/blob/main/docs/security/THREAT-MODEL.md" },
      { label: "Otwarte problemy (Open problems)", href: "https://github.com/konradcinkusz/exit-interview-agent/blob/main/docs/OPEN-PROBLEMS.md" },
    ],
    linksNote: "Te linki otwierają repozytorium projektu na GitHubie. Dokumenty są po angielsku.",
  },
  errors: {
    notFoundTitle: "Nie znaleziono strony",
    notFound: "Pod tym adresem nic nie ma.",
    errorTitle: "Coś poszło nie tak",
    error: "Nie udało się wyświetlić strony. Nic z tego, co wpisałeś/wpisałaś, nie zostało zapisane. Możesz spróbować ponownie albo wrócić na stronę główną.",
    retry: "Spróbuj ponownie",
    home: "Wróć na stronę główną",
  },
};
