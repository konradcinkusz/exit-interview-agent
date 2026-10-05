import { expect, test } from "@playwright/test";
import { FAKE_EMAIL, FAKE_PASSWORD } from "../support/constants.mjs";

// @smoke: the protected-flow charter for the scaffold is "a visitor can sign in, reach their account
// through the BFF, and nothing weaker than a verified token gets past the edge".

async function signIn(page: import("@playwright/test").Page, password = FAKE_PASSWORD) {
  await page.getByTestId("login-email").fill(FAKE_EMAIL);
  await page.getByTestId("login-password").fill(password);
  await page.getByTestId("login-submit").click();
}

test("a visitor who opens a protected page signs in and lands back on it as their account @smoke", async ({ page }) => {
  await page.goto("/account");
  await expect(page).toHaveURL(/\/login\?redirect=%2Faccount$/);

  await signIn(page);

  await expect(page).toHaveURL(/\/account$/);
  await expect(page.getByTestId("account-subject")).toHaveText("account-e2e");
});

test("tokens live only in HttpOnly cookies and never reach page JavaScript @smoke", async ({ page, context }) => {
  await page.goto("/login");
  await signIn(page);
  await expect(page.getByTestId("account-subject")).toBeVisible();

  const cookies = await context.cookies();
  const access = cookies.find((c) => c.name === "eia_access");
  expect(access?.httpOnly).toBe(true);
  expect(access?.sameSite).toBe("Strict");
  expect(await page.evaluate(() => document.cookie)).not.toContain("eia_");
  expect(await page.evaluate(() => JSON.stringify({ ...localStorage, ...sessionStorage }))).not.toContain("ey");
});

test("an unsigned token that decodes to the right claims is rejected at the edge @smoke", async ({ page, context }) => {
  const enc = (o: object) => Buffer.from(JSON.stringify(o)).toString("base64url");
  const forged = `${enc({ alg: "none" })}.${enc({ sub: "attacker", iss: "e2e-issuer", aud: "e2e-audience", exp: 9999999999 })}.`;
  await context.addCookies([{ name: "eia_access", value: forged, url: "http://localhost:4011" }]);

  await page.goto("/account");

  await expect(page).toHaveURL(/\/login\?redirect=%2Faccount$/);
});

test("a wrong password is refused with one generic message @smoke", async ({ page }) => {
  await page.goto("/login");
  await signIn(page, "not-the-password");

  await expect(page.getByTestId("login-error")).toHaveText("Invalid email or password.");
  await expect(page).toHaveURL(/\/login/);
});

test("signing out ends the session so the protected page is gated again @smoke", async ({ page }) => {
  await page.goto("/login");
  await signIn(page);
  await expect(page.getByTestId("account-subject")).toBeVisible();

  await page.getByTestId("logout").click();
  await expect(page).toHaveURL("/");
  await page.goto("/account");

  await expect(page).toHaveURL(/\/login\?redirect=%2Faccount$/);
});

test("runtime config exposes the proxy base path and no backend address @smoke", async ({ request }) => {
  const response = await request.get("/api/config");

  expect(response.status()).toBe(200);
  const text = await response.text();
  expect(JSON.parse(text)).toEqual({ apiBase: "/api/proxy", identity: { enabled: true } });
  expect(text).not.toContain("4010");
});

test("every page and API response carries the security header set @smoke", async ({ request }) => {
  for (const path of ["/login", "/api/config"]) {
    const headers = (await request.get(path)).headers();

    expect(headers["x-frame-options"], path).toBe("DENY");
    expect(headers["x-content-type-options"], path).toBe("nosniff");
    expect(headers["referrer-policy"], path).toBe("no-referrer");
    expect(headers["content-security-policy"], path).toContain("frame-ancestors 'none'");
    expect(headers["x-powered-by"], path).toBeUndefined();
  }
});
