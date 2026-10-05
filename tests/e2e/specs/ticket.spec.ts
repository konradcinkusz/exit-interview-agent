import { expect, test, type Page } from "@playwright/test";
import { STUB_URL, emailFor } from "../support/constants.mjs";
import { accountName, browserMemory, signIn } from "./support";

// Submission tickets for the CLI (ADR-0030, ADR-0048): minted through the BFF proxy, shown once, held in page memory only.

async function openCli(page: Page, name: string) {
  await page.goto("/login?redirect=%2Fcli");
  await signIn(page, name);
  await expect(page.getByTestId("ticket-create")).toBeVisible();
}

const configure = (request: import("@playwright/test").APIRequestContext, name: string, query: string) =>
  request.post(`${STUB_URL}/__test/ticket-config?email=${encodeURIComponent(emailFor(name))}&${query}`);

test("a signed-in account creates a ticket, sees it once with a countdown, and can copy it @smoke", async ({ page, context }) => {
  await context.grantPermissions(["clipboard-read", "clipboard-write"]);
  const name = accountName("ticket");
  await openCli(page, name);

  await page.getByTestId("ticket-create").click();

  const ticket = await page.getByTestId("ticket-value").innerText();
  expect(ticket).toMatch(/^[A-Za-z0-9_-]{43}$/);
  await expect(page.getByTestId("ticket-countdown")).toHaveText(/^(1[5-9]:\d\d|20:00)$/); // 15 to 20 minutes (ADR-0030 rounding)
  await expect(page.getByRole("heading", { name: "Your ticket" })).toBeFocused(); // focus moves to the result

  await page.getByTestId("ticket-copy").click();
  await expect(page.getByTestId("ticket-copy-status")).toContainText("Copied");
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(ticket);
});

test("the ticket is shown once: reloading, leaving and coming back never show it again @smoke", async ({ page }) => {
  const name = accountName("ticket");
  await openCli(page, name);
  await page.getByTestId("ticket-create").click();
  const ticket = await page.getByTestId("ticket-value").innerText();

  await page.reload();
  await expect(page.getByTestId("ticket-value")).toHaveCount(0);
  await expect(page.getByTestId("ticket-create")).toBeVisible();
  expect(await page.content()).not.toContain(ticket);

  await page.getByTestId("ticket-create").click();
  const second = await page.getByTestId("ticket-value").innerText();
  expect(second).not.toBe(ticket);

  await page.getByRole("link", { name: "Privacy" }).click();
  await page.goBack();
  await expect(page.getByTestId("ticket-value")).toHaveCount(0);
  expect(await page.content()).not.toContain(second);
});

test("the ticket is written nowhere: no storage, cookie, URL, or request other than the mint call @smoke", async ({ page, context }) => {
  const name = accountName("ticket");
  const requests: string[] = [];
  page.on("request", (r) => requests.push(`${r.method()} ${r.url()}`));
  await openCli(page, name);
  await page.getByTestId("ticket-create").click();
  const ticket = await page.getByTestId("ticket-value").innerText();

  const memory = await browserMemory(page);
  for (const [where, value] of Object.entries(memory)) expect(value, where).not.toContain(ticket);
  expect(JSON.stringify(await context.cookies())).not.toContain(ticket);
  expect(requests.join("\n")).not.toContain(ticket); // nothing the page sent, in URL form
  expect(requests.filter((r) => r.includes("/api/proxy/v1/tickets")).length).toBe(1);
});

test("the mint answer is never cacheable @smoke", async ({ page }) => {
  const name = accountName("ticket");
  await openCli(page, name);
  const [response] = await Promise.all([
    page.waitForResponse((r) => r.url().endsWith("/api/proxy/v1/tickets")),
    page.getByTestId("ticket-create").click(),
  ]);
  expect(response.status()).toBe(201);
  expect(response.headers()["cache-control"]).toBe("no-store");
});

test("clearing the ticket removes it from the page @smoke", async ({ page }) => {
  await openCli(page, accountName("ticket"));
  await page.getByTestId("ticket-create").click();
  await expect(page.getByTestId("ticket-value")).toBeVisible();

  await page.getByTestId("ticket-clear").click();

  await expect(page.getByTestId("ticket-value")).toHaveCount(0);
  await expect(page.getByTestId("ticket-gone")).toHaveText("Ticket cleared from this page.");
});

test("an expired ticket is cleared by itself and the page says so @smoke", async ({ page, request }) => {
  const name = accountName("ticket");
  await openCli(page, name);
  await configure(request, name, "ttl=3");

  await page.getByTestId("ticket-create").click();
  await expect(page.getByTestId("ticket-value")).toBeVisible();

  await expect(page.getByTestId("ticket-gone")).toContainText("expired", { timeout: 10_000 });
  await expect(page.getByTestId("ticket-value")).toHaveCount(0);
});

test("the fourth live ticket is refused with a plain explanation (TICKET_LIMIT) @smoke", async ({ page }) => {
  await openCli(page, accountName("ticket"));
  for (let i = 0; i < 3; i++) {
    await page.getByTestId("ticket-create").click();
    await expect(page.getByTestId("ticket-value")).toBeVisible();
    await page.getByTestId("ticket-clear").click(); // clearing the page does not revoke it: the service still counts it live
  }

  await page.getByTestId("ticket-create").click();

  await expect(page.getByTestId("ticket-error")).toContainText("maximum number of unused tickets");
  await expect(page.getByTestId("ticket-value")).toHaveCount(0);
});

test("the rate limit is explained with the wait the service asked for @smoke", async ({ page, request }) => {
  const name = accountName("ticket");
  await openCli(page, name);
  await configure(request, name, "rateLimit=1");

  await page.getByTestId("ticket-create").click();

  await expect(page.getByTestId("ticket-error")).toContainText("Too many requests");
  await expect(page.getByTestId("ticket-error")).toContainText("60 seconds");
});

test("the page lists only CLI commands that exist and says submitting is not released yet @smoke", async ({ page }) => {
  await openCli(page, accountName("ticket"));

  const commands = page.getByTestId("cli-commands");
  await expect(commands).toContainText("exit-interview demo --persona <id> [--seed <n>] [--out <dir>]");
  await expect(commands).toContainText("exit-interview personas");
  await expect(commands).not.toContainText("--ticket");
  await expect(commands).not.toContainText("submit");
  await expect(page.getByTestId("cli-submit-note")).toContainText("not in a released version");
  await expect(page.getByTestId("ticket-facts")).toContainText("Not tied to any employer");
});

test("a visitor who is not signed in is sent to sign in and returns to the page @smoke", async ({ page }) => {
  await page.goto("/cli");
  await expect(page).toHaveURL(/\/login\?redirect=%2Fcli$/);
});
