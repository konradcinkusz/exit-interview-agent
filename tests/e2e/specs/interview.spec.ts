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
  await answer.fill(text);
  await answer.press("Enter");
}

test("a visitor with a credit goes from consent to a result, copies a draft, downloads and deletes everything @smoke", async ({ page, context, request }) => {
  await context.grantPermissions(["clipboard-read", "clipboard-write"]);
  const name = accountName("interview");
  await configure(request, name, "credits=1");
  const seen = await watchViolations(page);

  await openInterview(page, name);
  await expect(page.getByText("Interview credits you have: 1.")).toBeVisible();
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

  const glassdoorCard = page.locator("li", { has: page.getByRole("heading", { name: "Glassdoor" }) });
  await glassdoorCard.getByRole("button", { name: "Copy: Glassdoor" }).click();
  await expect(glassdoorCard.getByRole("status")).toHaveText("Copied");
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(
    "Onboarding was chaotic, but the team helped. Contact with the manager was rare.",
  );

  const [recordDownload] = await Promise.all([
    page.waitForEvent("download"),
    page.getByRole("button", { name: "Download the record (JSON)" }).click(),
  ]);
  expect(recordDownload.suggestedFilename()).toBe("exit-interview-record.json");
  const record = JSON.parse(readFileSync((await recordDownload.path()) as string, "utf8")) as { topics: { onboarding: { rating: number } } };
  expect(record.topics.onboarding.rating).toBe(2);

  const [tilesDownload] = await Promise.all([
    page.waitForEvent("download"),
    page.getByRole("button", { name: "Download the draft texts (tiles.html)" }).click(),
  ]);
  const html = readFileSync((await tilesDownload.path()) as string, "utf8");
  expect(html).not.toMatch(/<script/i);
  expect(html).toContain("These are draft texts, not facts.");
  expect(html).toContain("Reddit");

  // Deleting is one deliberate step, and then nothing of the visit remains.
  await page.getByRole("button", { name: "Delete everything now" }).click();
  await page.getByRole("button", { name: "Yes, delete it" }).click();
  await expect(page.getByText("The interview was deleted. Nothing from it is kept.")).toBeVisible();
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
  await page.getByRole("button", { name: "Yes, delete it" }).click();
  await expect(page.getByText("The interview was deleted. Nothing from it is kept.")).toBeVisible();
  expect(await stubState(request, name)).toEqual({ credits: 0, session: null });
});

test("without a credit the page does not start, and the purchase goes to the payment page", async ({ page, request }) => {
  const name = accountName("interview");
  await configure(request, name, "credits=0");
  await openInterview(page, name);

  await expect(page.getByText("You have no interview credit. Buy one to start an interview.")).toBeVisible();
  await expect(page.getByRole("button", { name: "Continue" })).toBeDisabled();

  await page.route("https://checkout.example.invalid/**", (route) =>
    route.fulfill({ status: 200, contentType: "text/html", body: "<p>Checkout stub</p>" }),
  );
  await page.getByRole("button", { name: "Buy one interview" }).click();
  await page.waitForURL("https://checkout.example.invalid/session/e2e-1");
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

test("a lost session (410) returns to the start screen with the credit given back", async ({ page, request }) => {
  const name = accountName("interview");
  await configure(request, name, "credits=1");
  await openInterview(page, name);
  await startInterview(page);

  await configure(request, name, "expireNext=1");
  await say(page, "Hello.");
  await expect(alertWith(page, "expired or was lost when the service restarted.")).toContainText("Your credit has been returned.");
  await expect(page.getByRole("heading", { name: "Exit interview with an AI" })).toBeVisible();
  await expect(page.getByText("Interview credits you have: 1.")).toBeVisible();
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

test("the copy switches to Polish, and the page language follows", async ({ page, request }) => {
  const name = accountName("interview");
  await configure(request, name, "credits=1");
  await openInterview(page, name);

  await page.getByRole("radio", { name: "Polski" }).check();
  await expect(page.getByRole("heading", { name: "Rozmowa wyjściowa z AI" })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.lang)).toBe("pl");

  await page.getByRole("button", { name: "Dalej" }).click();
  await page.getByRole("checkbox").check();
  await page.getByRole("button", { name: "Rozpocznij rozmowę" }).click();
  await expect(page.getByRole("log", { name: "Rozmowa" })).toContainText("Dzień dobry. Jestem rozmówcą AI.");
  await say(page, "Wdrożenie było trudne.", "Twoja odpowiedź");
  await say(page, "Przełożony był rzadko dostępny.", "Twoja odpowiedź");
  await say(page, "Jeden przykład to odwołane spotkanie.", "Twoja odpowiedź");
  await expect(page.getByRole("heading", { name: "Zapis i szkice tekstów" })).toBeVisible();
  await expect(page.getByTestId("tiles-notice")).toContainText("To są propozycje tekstów, nie fakty.");
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
