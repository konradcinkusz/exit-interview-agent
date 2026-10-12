import { readFileSync } from "node:fs";
import AxeBuilder from "@axe-core/playwright";
import { expect, test, type APIRequestContext, type Page } from "@playwright/test";
import { STUB_URL, emailFor } from "../support/constants.mjs";
import { accountName, browserMemory, signIn, watchViolations } from "./support";

// The interview page (/interview) against the stub of interview-service (plan section 10). The journey is: credit, consent, chat,
// result, copy a draft, download, delete. The failure paths answer with the copy the catalog defines, and the page keeps nothing in
// the browser beyond the visit.

const CANARY = "Onboarding was chaotic for a very specific reason xq7";

const configure = (request: APIRequestContext, name: string, query: string) =>
  request.post(`${STUB_URL}/__test/interview-config?email=${encodeURIComponent(emailFor(name))}&${query}`);

const stubState = async (request: APIRequestContext, name: string) =>
  (await (await request.get(`${STUB_URL}/__test/interview?email=${encodeURIComponent(emailFor(name))}`)).json()) as {
    credits: number;
    session: { id: string; status: string; index: number } | null;
  };

/** Next's own route announcer is also a role=alert, so an alert is found by its words, not by its role alone. */
const alertWith = (page: Page, text: string) => page.getByRole("alert").filter({ hasText: text });

async function openInterview(page: Page, name: string) {
  await page.goto("/login?redirect=%2Finterview");
  await signIn(page, name);
  await expect(page.getByRole("heading", { name: "Exit interview with an AI" })).toBeVisible();
  // The page is server-rendered; the credit line changes only once it has hydrated and asked the service, so the controls work.
  await expect(page.getByText("Checking your credits…")).toHaveCount(0);
}

async function startInterview(page: Page) {
  await page.getByRole("button", { name: "Continue" }).click();
  await expect(page.getByRole("heading", { name: "Before we start" })).toBeFocused();
  await page.getByRole("checkbox").check();
  await page.getByRole("button", { name: "Start the interview" }).click();
  await expect(page.getByRole("log", { name: "Conversation" })).toContainText("Hello. I am an AI interviewer.");
}

async function say(page: Page, text: string, label = "Your answer") {
  const answer = page.getByLabel(label);
  // One answer at a time: while the interviewer is still replying, the page does not send (as for a person).
  await expect(page.getByRole("status").filter({ hasText: /The interviewer is typing|Rozmówca pisze/ })).toHaveCount(0);
  await answer.fill(text);
  await answer.press("Enter");
}

test("a visitor with a credit goes from consent to a result, copies a draft, downloads and deletes everything @smoke", async ({ page, context, request }) => {
  await context.grantPermissions(["clipboard-read", "clipboard-write"]);
  const name = accountName("interview");
  await configure(request, name, "credits=1");
  const seen = await watchViolations(page);

  await openInterview(page, name);
  await expect(page.getByTestId("credit-status")).toContainText("1 interview ready");
  await page.getByRole("radio", { name: "English" }).check();
  await page.getByLabel("How long did you work there?").selectOption("1y_3y");

  await startInterview(page);
  await say(page, `It was chaotic. ${CANARY}`);
  await expect(page.getByRole("log", { name: "Conversation" })).toContainText("Thank you. How would you describe");
  await say(page, "My manager was rarely available.");
  await say(page, "One 1:1 was cancelled three weeks in a row.");

  // The result: the notice first, the record as sentences, one card per draft.
  await expect(page.getByRole("heading", { name: "Your record and draft texts" })).toBeVisible();
  await expect(page.getByTestId("tiles-notice")).toContainText("These are draft texts, not facts.");
  await expect(page.getByText("Rating 2 of 5")).toBeVisible();
  await expect(page.getByText("Onboarding was chaotic.")).toBeVisible();

  // The Glassdoor draft: one copy button per section; the Pros copy puts exactly the Pros text on the clipboard.
  const glassdoorCard = page.getByRole("article", { name: "Glassdoor" });
  await glassdoorCard.getByRole("button", { name: "Copy: Glassdoor, Pros" }).click();
  await expect(glassdoorCard.getByRole("button", { name: "Copy: Glassdoor, Pros" }).locator("xpath=..").getByRole("status")).toHaveText("Copied");
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe("Onboarding was chaotic, but the team helped.");

  // One "Save a copy" control holds the three downloads.
  await page.getByTestId("save").getByText("Save a copy").click();
  const [recordDownload] = await Promise.all([
    page.waitForEvent("download"),
    page.getByRole("button", { name: "The record (JSON)" }).click(),
  ]);
  expect(recordDownload.suggestedFilename()).toBe("exit-interview-record.json");
  const record = JSON.parse(readFileSync((await recordDownload.path()) as string, "utf8")) as { topics: { onboarding: { rating: number } } };
  expect(record.topics.onboarding.rating).toBe(2);

  const [tilesDownload] = await Promise.all([
    page.waitForEvent("download"),
    page.getByRole("button", { name: "The draft texts, as a page (tiles.html)" }).click(),
  ]);
  const html = readFileSync((await tilesDownload.path()) as string, "utf8");
  expect(html).not.toMatch(/<script/i);
  expect(html).toContain("These are draft texts, not facts.");
  expect(html).toContain("Glassdoor");

  // Deleting is one deliberate step, and then nothing of the visit remains.
  await page.getByRole("button", { name: "Delete everything now" }).click();
  await page.getByRole("dialog", { name: "Delete this interview?" }).getByRole("button", { name: "Yes, delete it" }).click();
  await expect(page.getByText("The interview was deleted. Nothing from it is kept.")).toBeVisible();
  await expect(page.getByTestId("deleted-notice")).toContainText("The credit it used is not returned");
  expect(await stubState(request, name)).toEqual({ credits: 0, session: null });

  // Nothing the page could have kept: no storage, no cookie with the text, no URL with it, no call that carried it in a URL.
  const memory = await browserMemory(page);
  expect(memory.local).toBe("{}");
  expect(memory.session).toBe("{}");
  expect(JSON.stringify(memory)).not.toContain(CANARY);
  expect(seen.csp).toEqual([]);
  expect(seen.foreign).toEqual([]);
});

test("stop and delete asks once, then wipes the open interview @smoke", async ({ page, request }) => {
  const name = accountName("interview");
  await configure(request, name, "credits=1");
  await openInterview(page, name);
  await startInterview(page);

  await page.getByRole("button", { name: "Stop and delete" }).click();
  await expect(page.getByRole("button", { name: "Cancel" })).toBeFocused();
  await page.getByRole("button", { name: "Cancel" }).click();
  await expect(page.getByRole("log", { name: "Conversation" })).toBeVisible();

  await page.getByRole("button", { name: "Stop and delete" }).click();
  await page.getByRole("dialog", { name: "Delete this interview?" }).getByRole("button", { name: "Yes, delete it" }).click();
  await expect(page.getByText("The interview was deleted. Nothing from it is kept.")).toBeVisible();
  await expect(page.getByTestId("deleted-notice")).toContainText("The credit it used is not returned");
  expect(await stubState(request, name)).toEqual({ credits: 0, session: null });
});

test("without a credit the page does not start, and the purchase goes to the payment page", async ({ page, request }) => {
  const name = accountName("interview");
  await configure(request, name, "credits=0");
  await openInterview(page, name);

  await expect(page.getByTestId("credit-status")).toContainText("No interview available: you have no interview credit");
  await expect(page.getByRole("button", { name: "Continue" })).toBeDisabled();
  // The disabled button says why: its description is the reason, not a tooltip.
  await expect(page.getByRole("button", { name: "Continue" })).toHaveAccessibleDescription("Continue needs an interview credit.");

  await page.route("https://checkout.example.invalid/**", (route) =>
    route.fulfill({ status: 200, contentType: "text/html", body: "<p>Checkout stub</p>" }),
  );
  await page.getByRole("button", { name: "Buy one interview" }).click();
  await page.waitForURL(/checkout\.example\.invalid\/session\/cs_e2e_/);
});

test("a reply the model cannot take keeps the typed text and says the interview is still open", async ({ page, request }) => {
  const name = accountName("interview");
  await configure(request, name, "credits=1");
  await openInterview(page, name);
  await startInterview(page);

  await configure(request, name, "providerDown=1");
  const answer = page.getByLabel("Your answer");
  await answer.fill("My answer, kept.");
  await answer.press("Enter");
  await expect(alertWith(page, "The AI model is not responding right now.")).toContainText("Your interview is still open");
  await expect(answer).toHaveValue("My answer, kept.");
});

test("a lost session (410) returns to the start screen, and a new interview can start from there", async ({ page, request }) => {
  const name = accountName("interview");
  await configure(request, name, "credits=1");
  await openInterview(page, name);
  await startInterview(page);

  await configure(request, name, "expireNext=1");
  await say(page, "Hello.");
  // The same words for 404 and 410: the interview is no longer there, whatever the reason.
  await expect(alertWith(page, "This interview no longer exists")).toContainText("Your credits are shown below.");
  await expect(page.getByRole("heading", { name: "Exit interview with an AI" })).toBeVisible();
  await expect(page.getByTestId("credit-status")).toContainText("1 interview ready");

  await startInterview(page);
  await expect(page.getByRole("log", { name: "Conversation" })).toContainText("Hello. I am an AI interviewer.");
});

test("a second open interview is refused on the consent step, and a reload empties the transcript", async ({ page, request }) => {
  const name = accountName("interview");
  await configure(request, name, "credits=2");
  await openInterview(page, name);
  await startInterview(page);
  await say(page, "Still here.");
  await expect(page.getByRole("log", { name: "Conversation" })).toContainText("Still here.");

  await page.reload();
  await expect(page.getByRole("log", { name: "Conversation" })).toHaveCount(0);
  expect(await page.content()).not.toContain("Still here.");

  await page.getByRole("button", { name: "Continue" }).click();
  await page.getByRole("checkbox").check();
  await page.getByRole("button", { name: "Start the interview" }).click();
  await expect(alertWith(page, "You already have an open interview.")).toBeVisible();
});

test("the page follows the portal's language, and the interview's language is a separate choice", async ({ page, request }) => {
  const name = accountName("interview");
  await configure(request, name, "credits=1");
  await openInterview(page, name);

  // The header's switch turns the page Polish; the interview's language follows it by default.
  await page.getByTestId("language-switch").click();
  await expect(page.getByRole("heading", { name: "Rozmowa wyjściowa z AI" })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.lang)).toBe("pl");
  await expect(page.getByRole("radio", { name: "Polski" })).toBeChecked();
  await expect(page.getByTestId("credit-status")).toContainText("1 rozmowa gotowa");

  await page.getByRole("button", { name: "Dalej" }).click();
  await page.getByRole("checkbox").check();
  await page.getByRole("button", { name: "Rozpocznij rozmowę" }).click();
  const log = page.getByRole("log", { name: "Rozmowa" });
  await expect(log).toContainText("Dzień dobry. Jestem rozmówcą AI.");
  await expect(log).toHaveAttribute("lang", "pl");
  await say(page, "Wdrożenie było trudne.", "Twoja odpowiedź");
  await say(page, "Przełożony był rzadko dostępny.", "Twoja odpowiedź");
  await say(page, "Jeden przykład to odwołane spotkanie.", "Twoja odpowiedź");
  await expect(page.getByRole("heading", { name: "Zapis i szkice tekstów" })).toBeVisible();
  await expect(page.getByTestId("tiles-notice")).toContainText("To są propozycje tekstów, nie fakty.");
});

test("an English page can run a Polish interview: the conversation carries its own language", async ({ page, request }) => {
  const name = accountName("interview");
  await configure(request, name, "credits=1");
  await openInterview(page, name);

  await page.getByRole("radio", { name: "Polski" }).check();
  await expect(page.getByRole("heading", { name: "Exit interview with an AI" })).toBeVisible();
  await page.getByRole("button", { name: "Continue" }).click();
  await page.getByRole("checkbox").check();
  await page.getByRole("button", { name: "Start the interview" }).click();
  const log = page.getByRole("log", { name: "Conversation" });
  await expect(log).toContainText("Dzień dobry. Jestem rozmówcą AI.");
  await expect(log).toHaveAttribute("lang", "pl");
  expect(await page.evaluate(() => document.documentElement.lang)).toBe("en");
});

test("the interview pages have no axe violations on the start, consent, chat and result steps @smoke", async ({ page, request }) => {
  const name = accountName("interview");
  await configure(request, name, "credits=1");
  const check = async (label: string) => {
    const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
    const summary = results.violations.map((v) => `${v.id} (${v.impact}): ${v.nodes.map((n) => n.target.join(" ")).join(" | ")}`);
    expect(summary, `${label}: axe violations`).toEqual([]);
  };

  await openInterview(page, name);
  await check("start");
  await page.getByRole("button", { name: "Continue" }).click();
  await check("consent");
  await page.getByRole("checkbox").check();
  await page.getByRole("button", { name: "Start the interview" }).click();
  await expect(page.getByRole("log", { name: "Conversation" })).toBeVisible();
  await check("chat");
  await say(page, "Done with the questions.");
  await say(page, "Another answer.");
  await say(page, "The last one.");
  await expect(page.getByRole("heading", { name: "Your record and draft texts" })).toBeVisible();
  await check("result");
});

test("every interview answer through the BFF is no-store, including a refusal", async ({ page, request }) => {
  const name = accountName("interview");
  await configure(request, name, "credits=0");
  await openInterview(page, name);

  // The signed-in page's own cookies, read through the BFF: a credit balance, a start refused for lack of one, and an unknown id.
  const credits = await page.request.get("/api/proxy/v1/credits");
  expect(credits.status()).toBe(200);
  expect(credits.headers()["cache-control"]).toBe("no-store");

  const refused = await page.request.post("/api/proxy/v1/interviews", { data: { language: "en", tenure: "1y_3y" } });
  expect(refused.status()).toBe(402);
  expect(refused.headers()["cache-control"]).toBe("no-store");
  expect(await refused.json()).toMatchObject({ code: "payment_required" });

  const unknown = await page.request.get("/api/proxy/v1/interviews/int_doesnotexist");
  expect(unknown.status()).toBe(404);
  expect(unknown.headers()["cache-control"]).toBe("no-store");
});
