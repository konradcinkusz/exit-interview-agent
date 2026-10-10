# Consent wording: interview screen (PL and EN)

> **DRAFT for review by a lawyer. Status: NOT REVIEWED.** Not legal advice.
> Shown on the consent screen before the first interview (plan §3, contract §10). It must stay in step with the CLI disclosure
> (`src/ExitInterviewAgent.Providers/Disclosure.cs`, notice version 2) and with the privacy policy ([PL](privacy-policy.pl.md), [EN](privacy-policy.en.md)).
> Text in `[…]` is filled in by the owner. Consent text is versioned: change it, bump the version, and ask again.

Consent text version: `consent-v1` (draft, 2026-10-10). The policy version is `[policy-version]`.

## Structure of the screen

1. Disclosure (the text below). It is shown in full, before any question is asked.
2. Two required checkboxes, both unticked by default:
   - A (required): AI disclosure and recipient.
   - B (required): terms and privacy policy acceptance.
3. One optional checkbox, unticked by default:
   - C (optional): submission to employer signals.
4. Buttons: "Start the interview" (enabled when A and B are ticked) and "Not now" (no credit is used, nothing is kept).

## Polish (PL)

**Zanim zaczniemy: gdzie trafią Twoje odpowiedzi**

Tę rozmowę prowadzi system AI. Żeby ją przeprowadzić, cała rozmowa (każde pytanie i każda Twoja odpowiedź) jest wysyłana do dostawcy modelu **Anthropic** w celu wygenerowania kolejnych pytań, rekordu i szkiców tekstów. Warunki i zasady przechowywania danych u dostawcy obowiązują także. Projekt nie kontroluje ich i nie zweryfikował ich dla Twojego konta.

- Treść rozmowy jest przechowywana w pamięci serwera tylko na czas rozmowy. Nie zapisujemy jej w bazie danych ani w logach.
- Rozmowa wygasa po 30 minutach bez aktywności. Po zakończeniu wynik jest dostępny przez 30 minut, potem zostaje usunięty.
- Wynik to rekord (oceny i krótkie cytaty z Twoich słów, nazwiska maskowane) oraz szkice tekstów do publikacji. To **szkice do przeczytania i zmiany**. Nikt ich nie publikuje, a usługa nie wysyła ich nigdzie indziej. Publikujesz je sam, na własną odpowiedzialność.
- Usługa nie sprawdza prawdziwości tego, co mówisz, i nie daje porad prawnych.
- W każdej chwili możesz nacisnąć „Zatrzymaj i usuń”. Treść rozmowy i wynik zostaną usunięte.
- Jeśli chcesz, żeby nic nie opuszczało Twojego komputera, ta usługa nie jest dla Ciebie. [Do przeglądu: wording, czy wskazywać alternatywę.]

Więcej: Polityka prywatności i Regulamin (w aplikacji: strony `/privacy` i `/terms`, zob. CHECKLIST, sekcja B)

**[A] (wymagane)** Rozumiem, że rozmowę prowadzi AI i że cała rozmowa zostanie wysłana do Anthropic w celu jej przeprowadzenia.

**[B] (wymagane)** Akceptuję Regulamin i Politykę prywatności w wersji `[policy-version]`.

**[C] (opcjonalne)** Chcę, aby po zakończeniu mój rekord mógł trafić do zbiorczych zestawień sygnałów o pracodawcach. Zgadzam się na to dobrowolnie. Zestawienia pokazują wyniki tylko powyżej minimalnej liczby rekordów. Mogę wycofać tę zgodę i usunąć swoje zgłoszenie w każdej chwili. Bez tej zgody rozmowa i wynik działają tak samo.

[Przycisk] Zacznij rozmowę · [Przycisk] Nie teraz

## English (EN)

**Before we start: where your answers go**

This interview is run by an AI system. To run it, the whole conversation (every question and every answer you type) is sent to the model provider **Anthropic** to produce the next questions, the record and the draft texts. That provider's terms, data retention and training rules apply to it too. This project does not control them and has not verified them for your account.

- The conversation is held in server memory only for the length of the interview. We do not write it to the database or to logs.
- An interview expires after 30 minutes without activity. After it ends, the result is available for 30 minutes and then deleted.
- The result is a record (ratings and short quotes from your own words, names masked) and draft texts for publication. These are **drafts for you to read and change**. Nothing publishes them, and this service does not send them anywhere else. You publish them yourself, at your own responsibility.
- The service does not check whether what you say is true, and it does not give legal advice.
- You can press "Stop and delete" at any time. The conversation and the result are then deleted.

More: the privacy policy and the terms (in the app: the `/privacy` and `/terms` pages, see CHECKLIST, section B)

**[A] (required)** I understand that an AI system runs this interview and that the whole conversation will be sent to Anthropic to run it.

**[B] (required)** I accept the Terms and the Privacy Policy, version `[policy-version]`.

**[C] (optional)** I want my record to be possibly included in aggregated employer signals after the interview. I give this consent voluntarily. The aggregates show results only above a minimum number of records. I can withdraw this consent and delete my submission at any time. Without this consent, the interview and the result work the same way.

[Button] Start the interview · [Button] Not now

## Notes for the reviewer

- Wording follows the CLI disclosure: the same recipient, the same drafts notice, the same "stop and delete" control. Two differences are deliberate and must be checked: the CLI says the project does not send anything to any server of its own, while the hosted service does receive the conversation in memory, and it has a credit and payment step the CLI does not.
- The "Not now" path must use no credit and keep nothing (contract: `consent refused` gives `status: stopped` with nothing kept).
- Consent C is a separate, optional, unticked choice. It must not be a condition of the interview (Article 7(4) GDPR, for review).
- Withdrawal of consent during the interview is handled by the agent (ends the interview, nothing kept, no refund: terms §7.5).
- The `/privacy` and `/terms` pages are the web app routes. They must not go live with the drafts; see [CHECKLIST.md](CHECKLIST.md), section B.
