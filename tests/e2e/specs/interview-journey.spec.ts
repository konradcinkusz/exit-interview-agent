import { readFileSync } from "node:fs";
import AxeBuilder from "@axe-core/playwright";
import { expect, test, type APIRequestContext, type Page } from "@playwright/test";
import { STUB_URL, emailFor } from "../support/constants.mjs";
import { accountName, browserMemory, signIn, watchViolations } from "./support";

// The whole interview path against the stub (plan §10, W7): purchase through the payment page and the webhook, consent, chat,
// result, drafts, deletion, and the failure answers (402, 403, 429, 503). Every assertion is unconditional; a missing element fails.

const CANARY = "Onboarding was chaotic for a very specific reason xq7";
const CHECKOUT_ORIGIN = "https://checkout.example.invalid/";

const configure = (request: APIRequestContext, name: string, query: string) =>
  request.post(`${STUB_URL}/__test/interview-config?email=${encodeURIComponent(emailFor(name))}&${query}`);

const stubState = async (request: APIRequestContext, name: string) =>
  (await (await request.get(`${STUB_URL}/__test/interview?email=${encodeURIComponent(emailFor(name))}`)).json()) as {
    credits: number;
    session: { id: string; status: string; index: number } | null;
  };

/** The provider's signed event, delivered to the anonymous webhook route the way the provider would send it. */
async function deliverPaidCheckout(request: APIRequestContext, sessionId: string, eventId: string) {
  return request.post(`${STUB_URL}/api/v1/webhooks/payments`, {
    headers: { "stripe-signature": "t=1790000000,v1=stub" },
    data: { id: eventId, type: "checkout.session.completed", data: { object: { id: sessionId, payment_status: "paid" } } },
  });
}

const alertWith = (page: Page, text: string) => page.getByRole("alert").filter({ hasText: text });

async function openInterview(page: Page, name: string) {
  await page.goto("/login?redirect=%2Finterview");
  await signIn(page, name);
  await expect(page.getByRole("heading", { name: "Exit interview with an AI" })).toBeVisible();
}

async function consentAndStart(page: Page) {
  await page.getByRole("button", { name: "Continue" }).click();
  await expect(page.getByRole("heading", { name: "Before we start" })).toBeFocused();
  await page.getByRole("checkbox").check();
  await page.getByRole("button", { name: "Start the interview" }).click();
}

async function say(page: Page, text: string) {
  const answer = page.getByLabel("Your answer");
  await answer.fill(text);
  await answer.press("Enter");
}

test("a visitor with no credit buys one, interviews, reads the result, copies, downloads, deletes, and nothing is kept @smoke", async ({
  page,
  context,
  request,
}) => {
  await context.grantPermissions(["clipboard-read", "clipboard-write"]);
  const name = accountName("journey");
  await configure(request, name, "credits=0");
  const seen = await watchViolations(page);

  // (1) No credit: the start is closed, and the purchase opens the payment page.
  await openInterview(page, name);
  await expect(page.getByText("You have no interview credit. Buy one to start an interview.")).toBeVisible();
  await expect(page.getByRole("button", { name: "Continue" })).toBeDisabled();

  let checkoutUrl = "";
  await page.route(`${CHECKOUT_ORIGIN}**`, (route) => {
    checkoutUrl = route.request().url();
    return route.fulfill({
      status: 200,
      contentType: "text/html; charset=utf-8",
      body: "<!doctype html><meta charset=utf-8><title>Płatność stub</title><h1>Płatność stub</h1><p>Test payment page. Nothing is charged.</p>",
    });
  });
  await page.getByRole("button", { name: "Buy one interview" }).click();
  await page.waitForURL(/checkout\.example\.invalid\/session\/cs_e2e_/);
  await expect(page.getByRole("heading", { name: "Płatność stub" })).toBeVisible();

  // (2) The provider's paid event reaches the webhook; the balance is 1 when the person comes back.
  const sessionId = new URL(checkoutUrl).pathname.split("/").pop() ?? "";
  expect(sessionId).toMatch(/^cs_e2e_/);
  const delivered = await deliverPaidCheckout(request, sessionId, `evt_${sessionId}`);
  expect(delivered.status()).toBe(200);
  // A redelivery of the same event adds nothing (idempotent by event id).
  expect((await deliverPaidCheckout(request, sessionId, `evt_${sessionId}`)).status()).toBe(200);
  expect((await stubState(request, name)).credits).toBe(1);

  await page.goto("/interview");
  await expect(page.getByText("Interview credits you have: 1.")).toBeVisible();

  // (3) Consent, then the chat. The closing turn comes back before the record is ready; the page waits for the service's status.
  await page.getByRole("radio", { name: "English" }).check();
  await page.getByLabel("How long did you work there?").selectOption("1y_3y");
  await consentAndStart(page);
  await expect(page.getByRole("log", { name: "Conversation" })).toContainText("Hello. I am an AI interviewer.");
  await say(page, `It was chaotic. ${CANARY}`);
  await expect(page.getByRole("log", { name: "Conversation" })).toContainText("Thank you. How would you describe");
  await say(page, "My manager was rarely available.");
  await say(page, "One 1:1 was cancelled three weeks in a row.");
  await expect(page.getByRole("log", { name: "Conversation" })).toContainText("Thank you, that is all my questions.");

  // (4) The result: the notice first, the record as sentences, one card per draft.
  await expect(page.getByRole("heading", { name: "Your record and draft texts" })).toBeVisible();
  await expect(page.getByTestId("tiles-notice")).toContainText("These are draft texts, not facts.");
  await expect(page.getByText("Rating 2 of 5")).toBeVisible();
  await expect(page.getByText("Onboarding was chaotic.")).toBeVisible();
  const cards = page.locator("li", { has: page.getByRole("heading", { name: "Glassdoor" }) });
  await expect(cards).toHaveCount(1);
  await expect(page.locator("li", { has: page.getByRole("heading", { name: "Google review" }) })).toHaveCount(1);
  await expect(page.locator("li", { has: page.getByRole("heading", { name: "Reddit" }) })).toHaveCount(1);

  // (5) Copy works through the clipboard.
  const glassdoor = page.locator("li", { has: page.getByRole("heading", { name: "Glassdoor" }) });
  await glassdoor.getByRole("button", { name: "Copy: Glassdoor" }).click();
  await expect(glassdoor.getByRole("status")).toHaveText("Copied");
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(
    "Onboarding was chaotic, but the team helped. Contact with the manager was rare.",
  );

  // (6) The download of the drafts is a self-contained page with the notice in it and no script.
  const [tilesDownload] = await Promise.all([
    page.waitForEvent("download"),
    page.getByRole("button", { name: "Download the draft texts (tiles.html)" }).click(),
  ]);
  const html = readFileSync((await tilesDownload.path()) as string, "utf8");
  expect(html).not.toMatch(/<script/i);
  expect(html).toContain("These are draft texts, not facts.");
  expect(html).toContain("Reddit");

  // (7) Delete everything. The result is then gone from the service: the id is unknown, so the BFF answers 404 not_found.
  const sessionBefore = (await stubState(request, name)).session;
  expect(sessionBefore?.status).toBe("completed");
  await page.getByRole("button", { name: "Delete everything now" }).click();
  await page.getByRole("button", { name: "Yes, delete it" }).click();
  await expect(page.getByText("The interview was deleted. Nothing from it is kept.")).toBeVisible();
  expect(await stubState(request, name)).toEqual({ credits: 0, session: null });
  const afterDelete = await page.request.get(`/api/proxy/v1/interviews/${sessionBefore?.id}/result`);
  expect(afterDelete.status()).toBe(404);
  expect(await afterDelete.json()).toMatchObject({ code: "not_found" });

  // (8) Nothing the page could have kept: no storage, no cookie (including HttpOnly), no URL, no IndexedDB, and no foreign request
  // other than the payment page the test itself served.
  const memory = await browserMemory(page);
  expect(memory.local).toBe("{}");
  expect(memory.session).toBe("{}");
  expect(memory.url).not.toContain(CANARY);
  const cookies = await context.cookies();
  expect(JSON.stringify(cookies)).not.toContain(CANARY);
  expect(JSON.stringify(memory)).not.toContain(CANARY);
  expect(seen.csp).toEqual([]);
  expect(seen.foreign.filter((url) => !url.startsWith(CHECKOUT_ORIGIN))).toEqual([]);
});

test("declining the consent uses no credit and creates no interview", async ({ page, request }) => {
  const name = accountName("journey");
  await configure(request, name, "credits=1");
  await openInterview(page, name);

  await page.getByRole("button", { name: "Continue" }).click();
  await expect(page.getByRole("heading", { name: "Before we start" })).toBeFocused();
  await expect(page.getByRole("button", { name: "Start the interview" })).toBeDisabled();
  await page.getByRole("button", { name: "Back" }).click();

  await expect(page.getByRole("heading", { name: "Exit interview with an AI" })).toBeVisible();
  await expect(page.getByText("Interview credits you have: 1.")).toBeVisible();
  expect(await stubState(request, name)).toEqual({ credits: 1, session: null });
  await expect(page.getByRole("heading", { name: "Your record and draft texts" })).toHaveCount(0);
});

test("stopping in the chat ends with no record, and the credit stays used as the contract says", async ({ page, request }) => {
  const name = accountName("journey");
  await configure(request, name, "credits=1");
  await openInterview(page, name);
  await consentAndStart(page);
  await expect(page.getByRole("log", { name: "Conversation" })).toContainText("Hello. I am an AI interviewer.");

  await say(page, "Stop, please.");
  await expect(page.getByText("The interview was stopped. Nothing from it is kept, and there is no record.")).toBeVisible();
  await expect(page.getByRole("heading", { name: "Your record and draft texts" })).toHaveCount(0);
  expect(await stubState(request, name)).toEqual({ credits: 0, session: { id: expect.any(String), status: "stopped", index: 1 } });
});

test("a start refused for the rate limit says when to try again and keeps the credit", async ({ page, request }) => {
  const name = accountName("journey");
  await configure(request, name, "credits=1&rateLimited=1");
  await openInterview(page, name);
  await consentAndStart(page);

  await expect(alertWith(page, "Too many requests. Wait 42 seconds, then try again.")).toBeVisible();
  expect(await stubState(request, name)).toEqual({ credits: 1, session: null });
});

test("a paused service says so, and the credit is not used", async ({ page, request }) => {
  const name = accountName("journey");
  await configure(request, name, "credits=1&disabled=1");
  await openInterview(page, name);
  await consentAndStart(page);

  await expect(alertWith(page, "Interviews are paused for now. Your credit has not been used.")).toBeVisible();
  expect(await stubState(request, name)).toEqual({ credits: 1, session: null });
});

// The service's 403 email_not_verified reaches the page as the service's answer (the BFF passes a problem document with a code
// through, FRONTEND-BFF §5), and the page says how to fix it: a link to the confirmation page. The copy changed on purpose (ADR-0051).
test("an unverified email is refused: no interview starts and the credit is kept", async ({ page, request }) => {
  const name = accountName("journey");
  await configure(request, name, "credits=1&emailUnverified=1");
  await openInterview(page, name);
  await consentAndStart(page);

  const notice = alertWith(page, "To start an interview, confirm your email address in your account settings.");
  await expect(notice).toBeVisible();
  await expect(notice.getByRole("link", { name: "Confirm your email address" })).toHaveAttribute("href", "/verify-email");
  expect(await stubState(request, name)).toEqual({ credits: 1, session: null });
});

test("a reply refused for the rate limit keeps the typed answer and the interview open", async ({ page, request }) => {
  const name = accountName("journey");
  await configure(request, name, "credits=1");
  await openInterview(page, name);
  await consentAndStart(page);
  await expect(page.getByRole("log", { name: "Conversation" })).toContainText("Hello. I am an AI interviewer.");

  await configure(request, name, "replyRateLimited=1");
  const answer = page.getByLabel("Your answer");
  await answer.fill("My answer, kept.");
  await answer.press("Enter");
  await expect(alertWith(page, "Too many requests. Wait 30 seconds, then try again.")).toBeVisible();
  await expect(answer).toHaveValue("My answer, kept.");
  expect((await stubState(request, name)).session?.status).toBe("in_progress");
});

test("the chat works from the keyboard: consent, answer, send, and the stop dialog", async ({ page, request }) => {
  const name = accountName("journey");
  await configure(request, name, "credits=1");
  await openInterview(page, name);

  // Keyboard only from the start screen: the consent box by Space, the start by Enter.
  await page.getByRole("button", { name: "Continue" }).focus();
  await page.keyboard.press("Enter");
  await expect(page.getByRole("heading", { name: "Before we start" })).toBeFocused();
  await page.getByRole("checkbox").focus();
  await page.keyboard.press("Space");
  await expect(page.getByRole("checkbox")).toBeChecked();
  await page.getByRole("button", { name: "Start the interview" }).focus();
  await page.keyboard.press("Enter");
  await expect(page.getByRole("log", { name: "Conversation" })).toContainText("Hello. I am an AI interviewer.");

  // The answer field takes focus and sends on Enter; the typed text appears in the log.
  await page.getByLabel("Your answer").focus();
  await page.keyboard.type("Typed with the keyboard.");
  await page.keyboard.press("Enter");
  await expect(page.getByRole("log", { name: "Conversation" })).toContainText("Typed with the keyboard.");
  await expect(page.getByLabel("Your answer")).toHaveValue("");

  // The stop-and-delete control opens a confirmation; Cancel is focused and keeps the interview.
  await page.getByRole("button", { name: "Stop and delete" }).focus();
  await page.keyboard.press("Enter");
  await expect(page.getByRole("button", { name: "Cancel" })).toBeFocused();
  await page.keyboard.press("Enter");
  await expect(page.getByRole("log", { name: "Conversation" })).toBeVisible();
  expect((await stubState(request, name)).session?.status).toBe("in_progress");
});

test("the interview pages keep no axe violations on the purchase and the failure screens @smoke", async ({ page, request }) => {
  const name = accountName("journey");
  await configure(request, name, "credits=0");
  await openInterview(page, name);
  const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
  expect(
    results.violations.map((v) => `${v.id} (${v.impact}): ${v.nodes.map((n) => n.target.join(" ")).join(" | ")}`),
    "no-credit: axe violations",
  ).toEqual([]);

  await configure(request, name, "credits=1&rateLimited=1");
  await page.reload();
  await page.getByRole("button", { name: "Continue" }).click();
  await page.getByRole("checkbox").check();
  await page.getByRole("button", { name: "Start the interview" }).click();
  await expect(alertWith(page, "Too many requests.")).toBeVisible();
  const refused = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
  expect(
    refused.violations.map((v) => `${v.id} (${v.impact}): ${v.nodes.map((n) => n.target.join(" ")).join(" | ")}`),
    "rate-limited: axe violations",
  ).toEqual([]);
});
