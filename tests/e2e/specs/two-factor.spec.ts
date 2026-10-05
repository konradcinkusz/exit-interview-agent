import { expect, test, type Page } from "@playwright/test";
import { FAKE_PASSWORD, STUB_URL, emailFor } from "../support/constants.mjs";
import { accountName, signIn } from "./support";

// Two-factor sign-in (authservice TwoFactorController): the BFF keeps the challenge in an HttpOnly cookie, the page only
// ever sends a code (T-12, ADR-0050).

const twoFactorName = () => accountName("twofactor");

async function firstStep(page: Page, name: string, redirect = "%2Faccount") {
  await page.goto(`/login?redirect=${redirect}`);
  await signIn(page, name);
  await expect(page.getByTestId("twofactor-form")).toBeVisible();
}

test("an account with two-factor signs in with an authenticator code @smoke", async ({ page }) => {
  await firstStep(page, twoFactorName());
  await expect(page.getByTestId("twofactor-code")).toBeFocused();

  await page.getByTestId("twofactor-code").fill("123456");
  await page.getByTestId("twofactor-submit").click();

  await expect(page).toHaveURL(/\/account$/);
  await expect(page.getByTestId("account-subject")).toBeVisible();
});

test("the challenge stays in an HttpOnly cookie and never reaches page JavaScript @smoke", async ({ page, context }) => {
  await firstStep(page, twoFactorName());

  const challenge = (await context.cookies()).find((c) => c.name === "eia_2fa");
  expect(challenge?.httpOnly).toBe(true);
  expect(challenge?.sameSite).toBe("Strict");
  expect(await page.evaluate(() => document.cookie)).not.toContain("eia_2fa");
  expect(await page.evaluate(() => JSON.stringify({ ...localStorage, ...sessionStorage }))).not.toContain("ch-");
  expect((await context.cookies()).find((c) => c.name === "eia_access")).toBeUndefined(); // no session yet

  await page.getByTestId("twofactor-code").fill("123456");
  await page.getByTestId("twofactor-submit").click();
  await expect(page).toHaveURL(/\/account$/);
  expect((await context.cookies()).find((c) => c.name === "eia_2fa")).toBeUndefined();
});

test("a wrong code keeps the user on the step and a right one then works @smoke", async ({ page }) => {
  await firstStep(page, twoFactorName());

  await page.getByTestId("twofactor-code").fill("000000");
  await page.getByTestId("twofactor-submit").click();
  await expect(page.getByTestId("login-error")).toContainText("That code is not valid");
  await expect(page.getByTestId("twofactor-form")).toBeVisible();

  await page.getByTestId("twofactor-code").fill("123456");
  await page.getByTestId("twofactor-submit").click();
  await expect(page).toHaveURL(/\/account$/);
});

test("a recovery code signs in once and only once @smoke", async ({ page, context }) => {
  const name = twoFactorName();
  await firstStep(page, name);
  await page.getByTestId("twofactor-toggle").click();
  await expect(page.getByTestId("twofactor-code")).toBeFocused();
  await page.getByTestId("twofactor-code").fill("recovery-e2e-1");
  await page.getByTestId("twofactor-submit").click();
  await expect(page).toHaveURL(/\/account$/);

  await context.clearCookies();
  await firstStep(page, name);
  await page.getByTestId("twofactor-toggle").click();
  await page.getByTestId("twofactor-code").fill("recovery-e2e-1");
  await page.getByTestId("twofactor-submit").click();
  await expect(page.getByTestId("login-error")).toContainText("That code is not valid");
});

test("an expired challenge sends the user back to the first step with a message @smoke", async ({ page, request }) => {
  const name = twoFactorName();
  await firstStep(page, name);
  await request.post(`${STUB_URL}/__test/expire-challenge?email=${encodeURIComponent(emailFor(name))}`);

  await page.getByTestId("twofactor-code").fill("123456");
  await page.getByTestId("twofactor-submit").click();

  await expect(page.getByTestId("login-error")).toContainText("expired");
  await expect(page.getByTestId("login-email")).toBeVisible();
});

test("repeated wrong codes lock the account and say so, without creating a session @smoke", async ({ page, context }) => {
  await firstStep(page, twoFactorName());
  for (let i = 0; i < 5; i++) {
    await page.getByTestId("twofactor-code").fill("000000");
    await page.getByTestId("twofactor-submit").click();
    await expect(page.getByTestId("login-error")).toContainText("not valid");
  }
  await page.getByTestId("twofactor-code").fill("123456");
  await page.getByTestId("twofactor-submit").click();

  await expect(page.getByTestId("login-error")).toContainText("temporarily locked");
  expect((await context.cookies()).filter((c) => c.name === "eia_access")).toEqual([]);
});

test("the password step still rejects a wrong password without starting a second factor @smoke", async ({ page }) => {
  await page.goto("/login");
  await signIn(page, twoFactorName(), `${FAKE_PASSWORD}-wrong`);
  await expect(page.getByTestId("login-error")).toHaveText("Invalid email or password.");
  await expect(page.getByTestId("twofactor-form")).toHaveCount(0);
});
