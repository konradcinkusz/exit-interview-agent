import { expect, test } from "@playwright/test";
import { STUB_URL, emailFor } from "../support/constants.mjs";
import { accountName, browserMemory, hydrated, signIn } from "./support";

// The former employee's journey, end to end, in Polish (docs/ux/audit-2026-10-11, section (c), as built by the redesign): land,
// sign in, start, chat, read the result, delete everything. A Polish browser gets all of it in Polish without touching a switch,
// and never meets a developer page or an English sentence on the way (the conversation and the service's notice included).

const CANARY = "Wdrożenie było chaotyczne z bardzo konkretnego powodu qz4";

test.use({ locale: "pl-PL" });

test("sign in, start, chat, result, delete: the whole journey in Polish @smoke", async ({ page, request }) => {
  const name = accountName("podroz");
  await request.post(`${STUB_URL}/__test/interview-config?email=${encodeURIComponent(emailFor(name))}&credits=1`);

  // (1) Land: one headline, one button, in Polish, and what is kept.
  await page.goto("/");
  await expect(page.locator("html")).toHaveAttribute("lang", "pl");
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Przeprowadź rozmowę wyjściową z AI");
  await expect(page.getByTestId("home-kept")).toContainText("Anthropic");
  await page.getByTestId("interview-link").click();

  // (2) Sign in, with the reason on the page; straight to the interview afterwards.
  await expect(page.getByText("Zaloguj się, aby rozpocząć rozmowę wyjściową.")).toBeVisible();
  await signIn(page, name);
  await expect(page).toHaveURL(/\/interview$/);

  // (3) Start: the status line, the interview's language already Polish, Continue as the primary action.
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Rozmowa wyjściowa z AI");
  await expect(page.getByTestId("credit-status")).toContainText("1 rozmowa gotowa");
  await expect(page.getByRole("radio", { name: "Polski" })).toBeChecked();
  await page.getByLabel("Jak długo trwało Twoje zatrudnienie?").selectOption("3y_5y");
  await page.getByRole("button", { name: "Dalej" }).click();

  // Consent: the three facts first; the full terms on request.
  await expect(page.getByRole("heading", { name: "Zanim zaczniemy" })).toBeFocused();
  await expect(page.getByTestId("consent-key")).toContainText("Anthropic");
  await page.getByText("Przeczytaj pełne warunki").click();
  await expect(page.getByTestId("consent-terms-full")).toContainText("Nic nie sprawdza");
  await page.getByRole("checkbox").check();
  await page.getByRole("button", { name: "Rozpocznij rozmowę" }).click();

  // (4) Chat, with the progress line and the two ways out.
  const log = page.getByRole("log", { name: "Rozmowa" });
  await expect(log).toContainText("Dzień dobry. Jestem rozmówcą AI.");
  await expect(page.getByTestId("chat-progress")).toHaveText("Rozmowa · zwykle 10–15 minut · można zakończyć w każdej chwili");
  const say = async (text: string) => {
    await expect(page.getByRole("status").filter({ hasText: "Rozmówca pisze" })).toHaveCount(0);
    await page.getByLabel("Twoja odpowiedź").fill(text);
    await page.getByRole("button", { name: "Wyślij" }).click();
  };
  await say(CANARY);
  await expect(page.getByTestId("chat-progress")).toHaveText("Rozmowa · zwykle 10–15 minut · można zakończyć w każdej chwili");
  await expect(page.getByText(/z około \d/)).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Zakończ rozmowę" })).toBeEnabled();
  await expect(page.getByRole("button", { name: "Przerwij i usuń" })).toBeVisible();
  await say("Przełożony był rzadko dostępny.");
  await page.getByRole("button", { name: "Zakończ rozmowę" }).click();
  await expect(log).toContainText("Czy możemy już skończyć?");

  // (5) Result: one warning, the record, the drafts, one Save control, the time it stays readable.
  await expect(page.getByRole("heading", { name: "Zapis i szkice tekstów" })).toBeVisible();
  await expect(page.getByTestId("result-summary")).toContainText(/dostępny do około \d{2}:\d{2}/);
  await expect(page.getByTestId("result-summary")).toContainText("Ta rozmowa zużyła jeden kredyt.");
  await expect(page.getByTestId("result-warning")).toContainText("Szkice, nie fakty");
  await expect(page.getByTestId("tiles-notice")).toContainText("To są propozycje tekstów, nie fakty.");
  await expect(page.getByTestId("not-discussed")).toContainText("Nie poruszono:");
  await expect(page.locator("li", { has: page.getByRole("heading", { name: "Opinia Google" }) })).toHaveCount(1);
  await page.getByTestId("save").getByText("Zapisz kopię").click();
  const [download] = await Promise.all([page.waitForEvent("download"), page.getByRole("button", { name: "Zapis (JSON)" }).click()]);
  expect(download.suggestedFilename()).toBe("exit-interview-record.json");

  // (6) Delete: the dialog, Cancel first; then what happened to the credit and the next step.
  await page.getByRole("button", { name: "Usuń wszystko teraz" }).click();
  const dialog = page.getByRole("dialog", { name: "Usunąć tę rozmowę?" });
  await expect(dialog.getByRole("button")).toHaveText(["Anuluj", "Tak, usuń"]);
  await expect(dialog.getByRole("button", { name: "Anuluj" })).toBeFocused();
  await dialog.getByRole("button", { name: "Tak, usuń" }).click();
  const deleted = page.getByTestId("deleted-notice");
  await expect(deleted).toContainText("Rozmowa została usunięta. Nic z niej nie jest zachowywane.");
  await expect(deleted).toContainText("Kredyt, który zużyła, nie wraca");
  await expect(deleted).toContainText("Aby przeprowadzić kolejną rozmowę, kup ją poniżej.");
  await expect(page.getByTestId("credit-status")).toContainText("Brak dostępnej rozmowy");
  const state = (await (await request.get(`${STUB_URL}/__test/interview?email=${encodeURIComponent(emailFor(name))}`)).json()) as { session: unknown };
  expect(state.session).toBeNull();

  // Nothing of the conversation stayed in the browser, and the whole path spoke Polish: no English sentence from the portal or
  // the interview copy appeared in the page (the product name aside).
  expect(JSON.stringify(await browserMemory(page))).not.toContain(CANARY);
  await hydrated(page);
  const text = (await page.locator("body").innerText()).replaceAll("Exit Interview Agent", "");
  for (const english of ["Continue", "Your answer", "Delete everything", "Sign in", "Interview", "Privacy", "Development build"]) {
    expect(text, `"${english}" on the Polish page`).not.toContain(english);
  }
});
