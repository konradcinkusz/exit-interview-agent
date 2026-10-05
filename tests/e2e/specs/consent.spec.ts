import { expect, test } from "@playwright/test";
import { PRIVACY_VERSION, TERMS_VERSION } from "../support/constants.mjs";
import { accountName, signIn, stats } from "./support";

// Consent before anything else (ADR-0013): authservice versions Terms and Privacy; an account that has not accepted
// the versions in force sees the consent step first, and no API call goes through until it has.

test("an account that has not accepted lands on the consent step before the app @smoke", async ({ page, request }) => {
  const name = accountName("consent");
  await page.goto("/login?redirect=%2Faccount");
  await signIn(page, name);

  await expect(page).toHaveURL(/\/consent\?redirect=%2Faccount$/);
  await expect(page.getByTestId("consent-terms-version")).toHaveText(TERMS_VERSION);
  await expect(page.getByTestId("consent-privacy-version")).toHaveText(PRIVACY_VERSION);
  await expect(page.getByTestId("consent-accept")).toBeDisabled();

  await page.getByTestId("consent-terms").check();
  await expect(page.getByTestId("consent-accept")).toBeDisabled(); // both documents are required
  await page.getByTestId("consent-privacy").check();
  await page.getByTestId("consent-accept").click();

  await expect(page).toHaveURL(/\/account$/);
  await expect(page.getByTestId("account-subject")).toHaveText(`account-${name}`);
  const seen = await stats(request, name);
  expect(seen.consentAccepted).toBe(true);
  expect(seen.lastLocale).toMatch(/^[a-z]{2}(-[A-Z]{2})?$/); // the acceptance record carries the locale it was shown in
});

test("a signed-in account that has not accepted cannot reach a protected page or the API @smoke", async ({ page }) => {
  await page.goto("/login");
  await signIn(page, accountName("consent"));
  await expect(page).toHaveURL(/\/consent/);

  await page.goto("/account");
  await expect(page).toHaveURL(/\/consent\?redirect=%2Faccount$/);

  const api = await page.request.get("/api/proxy/v1/me");
  expect(api.status()).toBe(403);
  expect(await api.json()).toEqual({ error: "consent_required" });
});

test("declining ends the session and nothing is recorded @smoke", async ({ page, request }) => {
  const name = accountName("consent");
  await page.goto("/login");
  await signIn(page, name);
  await expect(page.getByTestId("consent-decline")).toBeVisible();

  await page.getByTestId("consent-decline").click();

  await expect(page).toHaveURL("/");
  await page.goto("/account");
  await expect(page).toHaveURL(/\/login\?redirect=%2Faccount$/);
  expect((await stats(request, name)).consentAccepted).toBe(false);
});

test("an account that already accepted goes straight to the app @smoke", async ({ page }) => {
  await page.goto("/login?redirect=%2Faccount");
  await signIn(page, accountName("accepted"));

  await expect(page).toHaveURL(/\/account$/);
});
