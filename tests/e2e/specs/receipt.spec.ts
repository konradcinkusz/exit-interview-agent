import { createHash } from "node:crypto";
import { expect, test } from "@playwright/test";
import { STUB_URL } from "../support/constants.mjs";
import { accountName, browserMemory, signIn } from "./support";

// Deletion by receipt code (ADR-0029, ADR-0049). Anonymous, the code only in the X-Receipt-Code header, the uniform answer.
const sha256 = (value: string) => createHash("sha256").update(value).digest("hex");

async function validCode(request: import("@playwright/test").APIRequestContext): Promise<string> {
  return ((await (await request.get(`${STUB_URL}/__test/receipt-code`)).json()) as { code: string }).code;
}

const requestLog = async (request: import("@playwright/test").APIRequestContext, code: string) =>
  (await (await request.get(`${STUB_URL}/__test/receipt-log?hash=${sha256(code)}`)).json()) as {
    authorization: boolean;
    cookie: boolean;
    search: string;
    headerNames: string[];
  }[];

test("a well-formed code gets the uniform answer, with no sign-in @smoke", async ({ page, request }) => {
  const code = await validCode(request);
  await page.goto("/delete-submission");

  await page.getByTestId("receipt-input").fill(code);
  await page.getByTestId("receipt-submit").click();

  const result = page.getByTestId("receipt-result");
  await expect(result).toContainText("If a submission with this receipt code existed, it is deleted now.");
  await expect(result).toContainText("does not tell you whether a submission existed");
  await expect(result).not.toContainText(/deleted successfully|was deleted|has been deleted/i);
});

test("the page says published figures drop the record at the next update, before and after a deletion @smoke", async ({ page, request }) => {
  const sentence = "Deleting your record removes it from the stored data now. Published figures drop it at the next update.";
  await page.goto("/delete-submission");
  await expect(page.getByTestId("receipt-published-figures")).toHaveText(sentence);

  await page.getByTestId("receipt-input").fill(await validCode(request));
  await page.getByTestId("receipt-submit").click();
  await expect(page.getByTestId("receipt-result")).toBeVisible();
  await expect(page.getByTestId("receipt-published-figures")).toHaveText(sentence);
});

test("the code travels only in the X-Receipt-Code header: no URL, no bearer, no cookie, nothing remembered @smoke", async ({ page, request, context }) => {
  const name = accountName("receipt");
  // Signed in on purpose: even then the request must carry no account.
  await page.goto("/login?redirect=%2Fdelete-submission");
  await signIn(page, name);
  await expect(page).toHaveURL(/\/delete-submission$/);
  const code = await validCode(request);
  const urls: string[] = [];
  page.on("request", (r) => urls.push(r.url()));

  await page.getByTestId("receipt-input").fill(code);
  await page.getByTestId("receipt-submit").click();
  await expect(page.getByTestId("receipt-result")).toBeVisible();

  const log = await requestLog(request, code);
  expect(log).toHaveLength(1);
  expect(log[0].authorization).toBe(false);
  expect(log[0].cookie).toBe(false);
  expect(log[0].search).toBe("");
  expect(log[0].headerNames).toEqual(["x-receipt-code"]);

  expect(urls.join("\n")).not.toContain(code);
  expect(page.url()).not.toContain(code);
  const memory = await browserMemory(page);
  for (const [where, value] of Object.entries(memory)) expect(value, where).not.toContain(code);
  expect(JSON.stringify(await context.cookies())).not.toContain(code);
  expect(await page.content()).not.toContain(code);
});

test("the input opts out of autofill and history and is emptied once sent @smoke", async ({ page, request }) => {
  const code = await validCode(request);
  await page.goto("/delete-submission");
  const input = page.getByTestId("receipt-input");

  await expect(input).toHaveAttribute("autocomplete", "off");
  await expect(input).toHaveAttribute("spellcheck", "false");
  await expect(input).not.toHaveAttribute("name", /.+/); // a nameless field cannot be put in a URL by a native form submit

  await input.fill(code);
  await page.getByTestId("receipt-submit").click();
  await page.getByTestId("receipt-again").click();
  await expect(page.getByTestId("receipt-input")).toHaveValue("");
});

test("a malformed code is a fixable error and says nothing was deleted @smoke", async ({ page }) => {
  await page.goto("/delete-submission");

  await page.getByTestId("receipt-input").fill("A".repeat(46)); // right length, wrong checksum
  await page.getByTestId("receipt-submit").click();

  await expect(page.getByTestId("receipt-error")).toContainText("not a valid receipt code");
  await expect(page.getByTestId("receipt-error")).toContainText("Nothing was deleted");
  await expect(page.getByTestId("receipt-result")).toHaveCount(0);
});

test("whitespace added by copy and paste is tolerated @smoke", async ({ page, request }) => {
  const code = await validCode(request);
  await page.goto("/delete-submission");

  await page.getByTestId("receipt-input").fill(`  ${code.slice(0, 20)}\n${code.slice(20)} `);
  await page.getByTestId("receipt-submit").click();

  await expect(page.getByTestId("receipt-result")).toBeVisible();
  expect(await requestLog(request, code)).toHaveLength(1);
});

test("when the limiter answers 429 the page says to wait and does not claim a deletion @smoke", async ({ page, request }) => {
  const code = await validCode(request);
  await request.post(`${STUB_URL}/__test/receipt-limit?count=1&hash=${sha256(code)}`);
  await page.goto("/delete-submission");

  await page.getByTestId("receipt-input").fill(code);
  await page.getByTestId("receipt-submit").click();

  await expect(page.getByTestId("receipt-error")).toContainText("Too many attempts");
  await expect(page.getByTestId("receipt-error")).toContainText("30 seconds");
  await expect(page.getByTestId("receipt-result")).toHaveCount(0);
});

test("the BFF refuses an oversized code without asking the backend, and a request without a code is a 400 @smoke", async ({ request }) => {
  const long = await request.delete("/api/receipts", { headers: { "X-Receipt-Code": "A".repeat(200) } });
  expect(long.status()).toBe(400);
  expect((await long.json()).code).toBe("INVALID_RECEIPT_CODE");
  expect(await requestLog(request, "A".repeat(200))).toHaveLength(0);

  const none = await request.delete("/api/receipts");
  expect(none.status()).toBe(400);
});

test("a code in the URL is not read: only the header counts @smoke", async ({ request }) => {
  const code = await validCode(request);
  const response = await request.delete(`/api/receipts?code=${code}`);

  expect(response.status()).toBe(400);
  expect(await requestLog(request, code)).toHaveLength(0);
});

test("the answer is never cacheable and the account page still says closing an account does not delete submissions @smoke", async ({ page, request }) => {
  const code = await validCode(request);
  const response = await request.delete("/api/receipts", { headers: { "X-Receipt-Code": code } });

  expect(response.status()).toBe(204);
  expect(response.headers()["cache-control"]).toBe("no-store");
  await page.goto("/delete-submission");
  await expect(page.getByText("Closing your account does not delete submissions")).toBeVisible();
});
