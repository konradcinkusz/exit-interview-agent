import { expect, test, type APIRequestContext, type Page } from "@playwright/test";
import { STUB_URL, emailFor } from "../support/constants.mjs";
import { accountName, browserMemory, signIn, watchViolations } from "./support";

// The Signals pages (T10b). The stub mirrors the Signals contract (stub-backend.mjs, contract note). What these tests protect:
// the page shows exactly what the API returned (the figure with its interval and n on one line, reliability, coverage), shows
// nothing the API withheld (no number for insufficient_data, no band named for a suppressed cut), answers "nothing to show"
// identically for every not-shown employer, never retries a 429 by itself, lets the browser reuse the answer within the batch,
// and has no control or word that could rank, sort or compare (docs/privacy/AGGREGATION.md section 8, docs/ux/UI-UX.md).

interface SignalsStats { requests: number; conditional: number; notModified: number }

/** Signs a fresh account in. By default lands on the account page, so that a test can configure the stub for this account before its first signals request. */
async function signedIn(page: Page, prefix = "signals", landing = "/account") {
  const name = accountName(prefix);
  await page.goto(`/login?redirect=${encodeURIComponent(landing)}`);
  await signIn(page, name);
  await expect(page).toHaveURL(new RegExp(`${landing.replace(/[/?]/g, "\\$&")}$`));
  return name;
}
const configure = (request: APIRequestContext, name: string, query: string) =>
  request.post(`${STUB_URL}/__test/signals-config?email=${encodeURIComponent(emailFor(name))}&${query}`);
const signalsStats = async (request: APIRequestContext, name: string) =>
  (await (await request.get(`${STUB_URL}/__test/signals-stats?email=${encodeURIComponent(emailFor(name))}`)).json()) as SignalsStats;

test("the list is alphabetical, says it does not rank employers, and pages with links @smoke", async ({ page }) => {
  await signedIn(page, "signals", "/signals");

  await expect(page.getByTestId("list-order")).toHaveText("Employers are listed alphabetically. This tool does not rank employers.");
  await expect(page.getByText("Page 1")).toBeVisible();
  const first = await page.getByTestId("employer-list").getByRole("link").allTextContents();
  expect(first).toHaveLength(20);
  expect(first).toEqual([...first].sort());
  expect(first[0]).toBe("demo-acme");
  await expect(page.getByTestId("aggregated-note")).toHaveText("Aggregated from records submitted by users of this tool. Employment is claimed, not verified.");

  await page.getByRole("link", { name: "Next page" }).click();
  await expect(page).toHaveURL(/\/signals\?page=2$/);
  await expect(page.getByText("Page 2")).toBeVisible();
  const second = await page.getByTestId("employer-list").getByRole("link").allTextContents();
  expect(second.length).toBeGreaterThan(0);
  expect(second.length).toBeLessThan(20);
  expect(second[0]! > first[19]!).toBe(true); // the pages continue the same alphabetical order
  await expect(page.getByRole("link", { name: "Next page" })).toHaveCount(0);

  await page.getByRole("link", { name: "Previous page" }).click();
  await expect(page).toHaveURL(/\/signals$/);
  await expect(page.getByText("Page 1")).toBeVisible();
});

test("the snapshot line says when, that figures change once per update, and that a deleted record is still counted until then @smoke", async ({ page }) => {
  await signedIn(page, "signals", "/signals");
  await expect(page.getByTestId("snapshot")).toContainText(/^Updated .* UTC\. Figures change once per update, not when someone submits\. A record deleted after this date is still counted until the next update\./);
  await expect(page.getByTestId("snapshot")).toContainText("A new update is published every 24 hours.");
});

test("when there is nothing to list the page says so and shows no list and no count @smoke", async ({ page, request }) => {
  const name = await signedIn(page);
  await configure(request, name, "empty=1");

  await page.goto("/signals");

  await expect(page.getByTestId("signals-empty")).toContainText("No employer has enough responses to show yet.");
  await expect(page.getByTestId("employer-list")).toHaveCount(0);
  await expect(page.locator("main")).not.toContainText(/\b\d+ (employers|responses)\b/);
  await page.goto("/signals/demo-acme"); // with no snapshot nothing is shown for an employer either
  await expect(page.getByTestId("nothing-to-show")).toBeVisible();
});

test("an employer shows each figure with its interval and n on one line, with reliability and coverage @smoke", async ({ page }) => {
  await signedIn(page);
  await page.goto("/signals/demo-acme");

  const onboarding = page.getByTestId("topic-onboarding");
  await expect(onboarding.getByTestId("stat-line").first()).toHaveText("3.82 (95% interval 3.40-4.20), 12 ratings");
  await expect(onboarding.getByTestId("reliability")).toContainText("moderate");
  await expect(onboarding.getByTestId("coverage")).toHaveText("high: the share of respondents who rated this topic.");
  await expect(onboarding).toContainText("A wide interval means early, not wrong.");
  await expect(page.getByTestId("respondents")).toContainText("10-24");
  await expect(page.getByTestId("employer-ref")).toContainText("demo-acme");
  await expect(page.getByTestId("aggregated-note")).toBeVisible();

  // the very low n of a small cell is shown, with its wide interval, not hidden
  await expect(page.getByTestId("topic-pay_vs_promises").getByTestId("stat-line").first()).toHaveText("3.20 (95% interval 2.30-4.10), 5 ratings");
  await expect(page.getByTestId("topic-pay_vs_promises").getByTestId("reliability")).toContainText("low");
  await expect(page.getByTestId("topic-pay_vs_promises").getByTestId("coverage")).toContainText("low:");

  // the six topics, in the API's order
  await expect(page.locator('[data-testid^="topic-"]')).toHaveCount(6);
  expect(await page.locator("section[data-testid^='topic-']").evaluateAll((els) => els.map((e) => e.getAttribute("data-testid")))).toEqual([
    "topic-onboarding", "topic-management", "topic-growth", "topic-pay_vs_promises", "topic-culture", "topic-reason_for_leaving",
  ]);
});

test("the distribution and the verification breakdown are real tables, and the verification is labelled claimed, not verified @smoke", async ({ page }) => {
  await signedIn(page);
  await page.goto("/signals/demo-acme");

  const distribution = page.getByTestId("topic-onboarding").getByTestId("distribution");
  await expect(distribution.locator("caption")).toHaveText("How the ratings were spread");
  await expect(distribution.getByRole("columnheader")).toHaveText(["Rating group", "Ratings"]);
  await expect(distribution.getByRole("row", { name: "Low (1-2) 0" })).toBeVisible();
  await expect(distribution.getByRole("row", { name: "Middle (3) 5" })).toBeVisible();
  await expect(distribution.getByRole("row", { name: "High (4-5) 7" })).toBeVisible();

  const verification = page.getByTestId("topic-growth").getByTestId("verification");
  await expect(page.getByTestId("topic-growth").getByTestId("verification-note")).toHaveText("Employment is claimed, not verified.");
  await expect(verification.getByRole("row", { name: "Not checked 12" })).toBeVisible();
  await expect(verification.getByRole("row", { name: "Confirmed 10" })).toBeVisible();

  // present only when the API sent them: growth has no distribution, onboarding has verification
  await expect(page.getByTestId("topic-growth").getByTestId("distribution")).toHaveCount(0);
});

test("a topic with insufficient data shows the sentence and no number at all @smoke", async ({ page }) => {
  await signedIn(page);
  await page.goto("/signals/demo-acme");

  for (const topic of ["management", "reason_for_leaving"]) {
    const section = page.getByTestId(`topic-${topic}`);
    await expect(section.getByTestId("not-enough")).toHaveText("Not enough responses to show this topic.");
    const text = (await section.innerText()).replace(/^[A-Za-z ]+\n/, ""); // without its own heading
    expect(text, `${topic} must carry no digit`).not.toMatch(/\d/);
    await expect(section.locator("table, svg, dl, [data-testid='stat-line']")).toHaveCount(0);
  }
});

test("a suppressed cut says it is hidden and names no band; a published cut shows its bands and 'No ratings.' for an empty one @smoke", async ({ page }) => {
  await signedIn(page);
  await page.goto("/signals/demo-acme");

  const suppressed = page.getByTestId("topic-onboarding").getByTestId("cut-seniority");
  await expect(suppressed).toContainText("This breakdown is hidden to protect small groups.");
  await expect(suppressed.locator("table")).toHaveCount(0);
  for (const band of ["Junior", "Mid-level", "Senior", "Management"]) await expect(suppressed).not.toContainText(band);

  const tenure = page.getByTestId("topic-onboarding").getByTestId("cut-tenure");
  await expect(tenure.locator("caption")).toHaveText("Breakdown by tenure");
  await expect(tenure.getByRole("columnheader")).toHaveText(["Group", "Figures"]);
  await expect(tenure.getByRole("row", { name: /1 to 3 years 3\.50 \(95% interval 2\.90-4\.10\), 6 ratings/ })).toBeVisible();
  await expect(tenure.getByRole("row", { name: "6 to 12 months No ratings." })).toBeVisible();
  await expect(tenure.getByRole("row", { name: /^Less than 6 months No ratings\.$/ })).toBeVisible();
});

test("an unknown employer and one with nothing to show get the same page and the same API answer @smoke", async ({ page }) => {
  await signedIn(page);

  const answers = [];
  for (const ref of ["demo-ghost", "demo-below-the-minimum"]) {
    const response = await page.request.get(`/api/proxy/v1/signals/employers/${ref}`);
    answers.push({ status: response.status(), body: await response.text(), cache: response.headers()["cache-control"], type: response.headers()["content-type"] });
  }
  expect(answers[0]).toEqual(answers[1]);
  expect(answers[0]!.status).toBe(404);
  expect(JSON.parse(answers[0]!.body).code).toBe("SIGNALS_EMPLOYER_NOT_FOUND");

  const pages = [];
  for (const ref of ["demo-ghost", "demo-below-the-minimum"]) {
    await page.goto(`/signals/${ref}`);
    await expect(page.getByTestId("nothing-to-show")).toBeVisible();
    pages.push(await page.locator("main").innerText());
  }
  expect(pages[0]).toBe(pages[1]);
  expect(pages[0]).toContain("There is nothing to show for this employer.");
  expect(pages[0]!.toLowerCase()).not.toMatch(/unknown|not found|does not exist/);
});

test("a reference that is not well formed never reaches the backend, and looks like every other 'nothing to show' @smoke", async ({ page, request }) => {
  const name = await signedIn(page);
  const before = (await signalsStats(request, name)).requests;
  expect(before).toBe(0);

  await page.goto("/signals/Not%20A%20Ref");
  await expect(page.getByTestId("nothing-to-show")).toBeVisible();
  await page.goto("/signals/ab");
  await expect(page.getByTestId("nothing-to-show")).toBeVisible();
  const bff = await page.request.get("/api/proxy/v1/signals/employers/Bad_Ref");
  expect(bff.status()).toBe(400);
  expect((await bff.json()).code).toBe("SIGNALS_INVALID_EMPLOYER_REF");
  expect(bff.headers()["cache-control"]).toBe("no-store");

  expect((await signalsStats(request, name)).requests - before).toBe(0);
});

test("a 429 says how long to wait, does not retry by itself, and the button works once the wait is over @smoke", async ({ page, request }) => {
  const name = await signedIn(page);
  await configure(request, name, "limit=1&retryAfter=2");
  const before = (await signalsStats(request, name)).requests;

  await page.goto("/signals/demo-acme");

  await expect(page.getByTestId("signals-failure").getByRole("alert")).toContainText("Too many requests");
  await expect(page.getByTestId("signals-wait")).toHaveText("You can try again in about 2 seconds.");
  const retry = page.getByTestId("signals-retry");
  await expect(retry).toBeDisabled();
  await expect(retry).toBeEnabled({ timeout: 10_000 });
  expect((await signalsStats(request, name)).requests - before, "nothing retried by itself").toBe(1);
  await expect(page.getByTestId("topic-onboarding")).toHaveCount(0);

  await retry.click();
  await expect(page.getByTestId("topic-onboarding")).toBeVisible();
  expect((await signalsStats(request, name)).requests - before).toBe(2);
});

test("the BFF passes the validators through and a conditional request is answered 304 with no body @smoke", async ({ page, request }) => {
  const name = await signedIn(page);

  const first = await page.request.get("/api/proxy/v1/signals/employers/demo-acme");
  expect(first.status()).toBe(200);
  const headers = first.headers();
  expect(headers["etag"]).toMatch(/^W\/"s\d+-demo-acme"$/);
  expect(headers["cache-control"]).toMatch(/^private, max-age=\d+$/);
  expect(headers["cache-control"]).not.toContain("public");
  expect(headers["vary"]).toContain("Cookie");
  expect(headers["last-modified"]).toBeTruthy();
  expect(Number(/max-age=(\d+)/.exec(headers["cache-control"]!)![1])).toBeLessThanOrEqual(7 * 24 * 3600);

  const second = await page.request.get("/api/proxy/v1/signals/employers/demo-acme", { headers: { "If-None-Match": headers["etag"]! } });
  expect(second.status()).toBe(304);
  expect(await second.body()).toHaveLength(0);
  expect(second.headers()["etag"]).toBe(headers["etag"]);
  expect(second.headers()["cache-control"]).toMatch(/^private, max-age=\d+$/);

  const stale = await page.request.get("/api/proxy/v1/signals/employers/demo-acme", { headers: { "If-None-Match": 'W/"s0-demo-acme"' } });
  expect(stale.status()).toBe(200);

  const stats = await signalsStats(request, name);
  expect(stats.conditional).toBe(2);
  expect(stats.notModified).toBe(1);
});

test("other BFF answers stay no-store, and a 429 passes Retry-After on @smoke", async ({ page, request }) => {
  const name = await signedIn(page);

  expect((await page.request.get("/api/proxy/v1/me")).headers()["cache-control"]).toBe("no-store");
  expect((await page.request.get("/api/proxy/v1/signals/other")).headers()["cache-control"]).toBe("no-store");
  expect((await page.request.get("/signals")).headers()["cache-control"]).toBe("no-store");
  expect((await page.request.get("/signals/demo-acme")).headers()["cache-control"]).toBe("no-store");

  await configure(request, name, "limit=1&retryAfter=7");
  const limited = await page.request.get("/api/proxy/v1/signals/employers");
  expect(limited.status()).toBe(429);
  expect(limited.headers()["retry-after"]).toBe("7");
  expect(limited.headers()["cache-control"]).toBe("no-store");
  expect(limited.headers()["etag"]).toBeUndefined();
});

test("a second visit within the batch costs no request: the browser reuses the answer @smoke", async ({ page, request }) => {
  const name = await signedIn(page);
  await page.goto("/signals/demo-acme");
  await expect(page.getByTestId("topic-onboarding")).toBeVisible();
  const afterFirst = (await signalsStats(request, name)).requests;

  await page.goto("/privacy");
  await page.goto("/signals/demo-acme");
  await expect(page.getByTestId("topic-onboarding")).toBeVisible();

  expect((await signalsStats(request, name)).requests).toBe(afterFirst);
});

test("the page is gated: signed out, an employer page goes to sign-in and back, and the API answers 401 @smoke", async ({ page, request }) => {
  await page.goto("/signals/demo-acme");
  await expect(page).toHaveURL(/\/login\?redirect=%2Fsignals%2Fdemo-acme$/);
  expect((await request.get("/api/proxy/v1/signals/employers", { maxRedirects: 0 })).status()).toBe(401);
});

test("the navigation has the entry, and it opens the list once signed in @smoke", async ({ page }) => {
  await page.goto("/login");
  await signIn(page, accountName("signals-nav"));
  await expect(page).toHaveURL(/\/account$/);
  await page.getByRole("navigation", { name: "Main" }).getByRole("link", { name: "Employer signals" }).click();
  await expect(page).toHaveURL(/\/signals$/);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Employer signals");
});

test("the explainer says how to read a figure and that nothing is ranked @smoke", async ({ page }) => {
  await signedIn(page, "signals", "/signals");
  const explainer = page.getByTestId("signals-explainer");
  await expect(explainer).toContainText("A wide interval means early, not wrong.");
  await expect(explainer).toContainText("no overall score for an employer");
  await expect(explainer).toContainText("This tool does not compare employers");
});

test("API strings are rendered as text only: markup in a band, a topic or a group runs nothing @smoke", async ({ page }) => {
  await signedIn(page);
  const seen = await watchViolations(page);
  await page.goto("/signals/demo-hostile");

  await expect(page.getByTestId("respondents")).toContainText('<img src=x onerror="window.__pwned=1">');
  await expect(page.locator("main img")).toHaveCount(0);
  expect(await page.evaluate(() => (window as unknown as { __pwned?: number }).__pwned)).toBeUndefined();
  expect(seen.csp).toEqual([]);
});

test("there is nothing on the pages that could rank, sort, score or compare @smoke", async ({ page }) => {
  await signedIn(page);
  for (const path of ["/signals", "/signals/demo-acme"]) {
    await page.goto(path);
    await expect(page.locator("main").getByRole("heading", { level: 1 })).toBeVisible();
    await expect(page.locator("main").locator("button, select, input, textarea, [role=combobox], [role=searchbox], [role=search], [aria-sort], form")).toHaveCount(0);
    await expect(page.locator("[data-testid*='score' i], [data-testid*='rank' i], [data-testid*='average' i], [data-testid*='percentile' i], [data-testid*='trend' i]")).toHaveCount(0);
  }
  await page.goto("/signals/demo-acme");
  const text = (await page.locator("main").innerText()).toLowerCase();
  for (const word of ["best", "worst", "rank", "score", "average", "percentile", "trend", "compare", "better", "worse"]) {
    expect(text, `employer page must not contain "${word}"`).not.toMatch(new RegExp(`\\b${word}`));
  }
  expect(await page.locator("main").innerHTML()).not.toMatch(/[↑↓▲▼⬆⬇]/);
});

test("no employer reference or figure is kept by the page, in its title or in a URL it did not get @smoke", async ({ page }) => {
  await signedIn(page);
  await page.goto("/signals/demo-acme");
  await expect(page.getByTestId("topic-onboarding")).toBeVisible();

  expect(await page.title()).toMatch(/^Employer signals( · |$)/);
  expect(await page.title()).not.toContain("demo-acme");
  const memory = await browserMemory(page);
  for (const where of [memory.local, memory.session, memory.cookie, memory.databases]) {
    expect(where).not.toContain("demo-acme");
    expect(where).not.toContain("3.82");
  }
});

test("the layout holds at 320 px: a topic's tables do not scroll the page sideways @smoke", async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 640 });
  await signedIn(page);
  for (const path of ["/signals", "/signals/demo-acme", "/signals/demo-gamma"]) {
    await page.goto(path);
    await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
    await page.waitForLoadState("networkidle");
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow, `${path} horizontal overflow at 320px`).toBeLessThanOrEqual(0);
  }
});
