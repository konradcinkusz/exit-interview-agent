# Polityka prywatności: aplikacja webowa wywiadu wyjściowego

> **SZKIC do przeglądu prawnika. Status: NOT REVIEWED.**
> To nie jest porada prawna ani gotowy dokument. Dokument opisuje, jak ma działać usługa, i wskazuje, co trzeba potwierdzić.
> Teksty w nawiasach kwadratowych `[…]` to pola do uzupełnienia przez właściciela albo prawnika. Nie wolno publikować tego tekstu przed przeglądem (zob. [CHECKLIST.md](CHECKLIST.md)).
> Wersja szkicu: 0.1, 2026-10-10. Wersja angielska: [privacy-policy.en.md](privacy-policy.en.md).

## 1. Kto jest administratorem

- Administrator: `[ADMINISTRATOR]` (nazwa, adres, identyfikator podatkowy).
- Kontakt w sprawach danych: `[KONTAKT]`.
- Inspektor ochrony danych: `[IOD albo: nie wyznaczono, do ustalenia]`.

Założenie do przeglądu: administrator odpowiada za dane konta i płatności. Treść rozmowy usługa przetwarza jako podmiot pośredniczący przy przekazaniu do dostawcy modelu. Podział ról jest pytaniem do prawnika (zob. CHECKLIST, L3).

## 2. Jakie dane zbieramy i po co

| Kategoria | Co zawiera | Cel | Gdzie jest przechowywana |
|---|---|---|---|
| Konto | adres e-mail, identyfikator konta, sposób logowania, zgody | logowanie, weryfikacja adresu, dowód zgód | usługa tożsamości (authservice), prowadzona przez `[ADMINISTRATOR]` |
| Płatność | zamówienie, liczba kupionych rozmów, status płatności, identyfikator płatności u dostawcy | przyznanie rozmowy, rozliczenie, faktura | baza usługi; dane karty płatniczej **nie trafiają do nas**, obsługuje je `[DOSTAWCA PŁATNOŚCI]` |
| Treść rozmowy | Twoje odpowiedzi, pytania prowadzącego, wypowiedzi zapisane w trakcie rozmowy | przeprowadzenie rozmowy | **tylko w pamięci serwera** na czas rozmowy (zob. pkt 3 i 4); wysyłana do dostawcy modelu (pkt 5) |
| Wynik | rekord ustrukturyzowany (oceny i krótkie cytaty) oraz szkice tekstów | pokazanie wyniku Tobie | **tylko w pamięci serwera** przez 30 minut po zakończeniu; potem usunięty |
| Zgłoszenie do sygnałów pracodawców | rekord, o ile wyraźnie na to wyraziłeś zgodę | zbiorcze, anonimowe z założenia zestawienia (agregaty powyżej minimalnej liczby rekordów) | baza usługi, do usunięcia (pkt 4) |
| Dane techniczne | znaczniki czasu, liczba wywołań modelu i szacunkowa liczba tokenów, adres IP do ograniczania liczby żądań | bezpieczeństwo, limity, koszty | logi i liczniki bez treści rozmowy; `[okres, do ustalenia]` |

Dane nie są używane do profilowania ani do reklamy. Aplikacja nie korzysta z narzędzi analitycznych na treści rozmowy.

Podstawy prawne (do potwierdzenia przez prawnika): konto i płatność, umowa z Tobą (art. 6 ust. 1 lit. b RODO); zgoda na zgłoszenie do sygnałów pracodawców, dobrowolna i cofalna (art. 6 ust. 1 lit. a RODO); treść rozmowy, jeśli zawiera dane szczególnych kategorii, wymaga osobnej podstawy (art. 9 RODO), patrz CHECKLIST, L4.

## 3. Czego nie przechowujemy (deklaracja projektowa)

- Treści rozmowy nie zapisujemy w bazie danych ani w logach. Zapis treści nie jest wykonywany przez kod usługi poza pamięcią procesu na czas rozmowy.
- Wynik (rekord i szkice) jest zwracany tylko Tobie. Nie publikujemy go i nie wysyłamy nikomu innemu.
- Logi, śledzenie i metryki nie zawierają treści rozmowy, nazwisk ani adresów e-mail.

> Ta deklaracja ma być sprawdzona testami przed startem. Jeśli kod zapisuje treść, deklaracja jest nieprawdziwa i ten punkt trzeba cofnąć.

## 4. Jak długo przechowujemy dane

| Dane | Okres | Co się dzieje po okresie |
|---|---|---|
| Treść rozmowy (w pamięci) | do zakończenia rozmowy albo 30 minut bez aktywności | usuwana z pamięci |
| Wynik (rekord i szkice) | 30 minut po zakończeniu rozmowy | usuwany; możesz usunąć go wcześniej |
| Zgłoszenie do sygnałów pracodawców | do Twojego żądania usunięcia albo do upływu `[maksymalny wiek rekordu, do ustalenia]` | usuwane automatycznie lub na żądanie |
| Dane konta | do usunięcia konta albo `[okres, do ustalenia]` | usuwane (do przeglądu: konto może wymagać zachowania dowodu zgód przez dłuższy czas) |
| Dane płatności i faktury | `[okres wymagany przepisami podatkowymi, do ustalenia przez prawnika lub księgowego]` | usuwane po upływie okresu |
| Znaczniki zgłoszeń (jednokierunkowy skrót pary konto–pracodawca) | `[okres, do ustalenia]` | usuwane po upływie okresu |
| Logi i liczniki bez treści | `[okres, do ustalenia]` | usuwane po upływie okresu |

Usunięcie na żądanie: przycisk „Zatrzymaj i usuń” w trakcie rozmowy usuwa treść rozmowy i wynik. Usunięcie zgłoszenia następuje kodem pokwitowania albo przez `[KONTAKT]`. Usługa nie ujawnia, czy zgłoszenie istniało.

## 5. Podmioty przetwarzające dane i odbiorcy

| Odbiorca | Co otrzymuje | Rola (do przeglądu) |
|---|---|---|
| **Anthropic** (dostawca modelu AI, `Anthropic, PBC`) | treść rozmowy potrzebna do prowadzenia rozmowy i do napisania wyniku i szkiców | podmiot przetwarzający albo odrębny administrator, zależnie od warunków dostawcy dla klienta API; **warunki i zasady przechowywania u dostawcy nie zostały przez projekt zweryfikowane dla tego konta**; umowa powierzenia `[do zawarcia albo potwierdzenia]` |
| `[DOSTAWCA PŁATNOŚCI]` | dane zamówienia i płatności | administrator lub podmiot przetwarzający, do ustalenia |
| `[HOSTING]` (infrastruktura serwerów) | dane przechowywane na serwerach | podmiot przetwarzający, `[umowa powierzenia]` |
| Operator usługi tożsamości | dane konta | `[do ustalenia]` |

Nie sprzedajemy danych i nie udostępniamy ich reklamodawcom.

## 6. Przekazywanie danych poza Europejski Obszar Gospodarczy

Treść rozmowy jest przekazywana do dostawcy modelu. `[Miejsce przetwarzania u dostawcy: do potwierdzenia]`. Jeżeli przetwarzanie odbywa się poza EOG, podstawą przekazania mają być `[standardowe klauzule umowne lub inna podstawa, do ustalenia przez prawnika]`. Projekt nie zweryfikował źródeł prawnych dotyczących tego przekazania (zob. `docs/legal/CONSIDERATIONS.md`, §6).

## 7. Twoje prawa

Masz prawo do: dostępu do danych, sprostowania, usunięcia, ograniczenia przetwarzania, przenoszenia, sprzeciwu wobec przetwarzania opartego na prawnie uzasadnionym interesie oraz cofnięcia zgody w dowolnym momencie. Cofnięcie zgody nie wpływa na zgodność z prawem przetwarzania przed cofnięciem.

Masz też prawo wniesienia skargi do Prezesa Urzędu Ochrony Danych Osobowych. Zgłoszenie do administratora: `[KONTAKT]`.

Pełna obsługa tych praw po stronie usługi (procedura, terminy, wyjątki) wymaga przeglądu prawnika.

## 8. Zautomatyzowane przetwarzanie

Rozmowę prowadzi system AI. Na początku rozmowy jesteś o tym informowany. Wyniki są szkicami do przeczytania przez Ciebie. Nie wywołują skutków prawnych wobec Ciebie ani innych osób i nie są decyzją o zatrudnieniu, świadczeniu ani innej sprawie. Usługa nie weryfikuje faktów, które podajesz.

## 9. Osoby trzecie w rozmowie

W rozmowie mogą pojawić się nazwiska lub dane osób trzecich (przełożonych, współpracowników). Usługa stara się je wykrywać i maskować, ale nie daje na to gwarancji. Nie podawaj danych osób trzecich, jeśli nie musisz. Informacje o osobach trzecich, których nie da się ukryć, mogą wymagać osobnej informacji dla tych osób (zob. CHECKLIST, L7).

## 10. Bezpieczeństwo

Dostęp do usługi wymaga zalogowania. Klucze dostawców są przechowywane tylko po stronie serwera. Ruch idzie szyfrowanym połączeniem. Żadne zabezpieczenie nie jest absolutne: w razie naruszenia poinformujemy Cię i organ nadzorczy zgodnie z RODO.

## 11. Zmiany tej polityki

O istotnych zmianach poinformujemy przy logowaniu. Zgody z poprzednią wersją polityki są zapisywane z jej numerem.

## 12. Zastrzeżenie

Ten dokument jest szkicem. Nie zastępuje konsultacji z prawnikiem i nie jest poradą prawną.
