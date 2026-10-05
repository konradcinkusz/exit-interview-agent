import { expect, test, type Page } from "@playwright/test";
import { FAKE_PASSWORD, PRIVACY_VERSION, TERMS_VERSION, emailFor } from "../support/constants.mjs";
import { accountName, signIn } from "./support";

// Sign-up and email verification as authservice defines them. The BFF forwards; the portal never issues a session at sign-up.

async function fillRegistration(page: Page, name: string, password = FAKE_PASSWORD) {
  await page.goto("/register");
  await page.getByTestId("register-email").fill(emailFor(name));
  await page.getByTestId("register-password").fill(password);
  await page.getByTestId("register-terms").check();
  await page.getByTestId("register-privacy").check();
}

test("a visitor registers, sees the versions they accept, and signs in @smoke", async ({ page }) => {
  const name = accountName("reg");
  await page.goto("/register");
  await expect(page.getByTestId("register-submit")).toBeDisabled();
  await expect(page.getByText(TERMS_VERSION).first()).toBeVisible();
  await expect(page.getByText(PRIVACY_VERSION).first()).toBeVisible();

  await fillRegistration(page, name);
  await page.getByTestId("register-submit").click();

  await expect(page.getByTestId("register-done")).toContainText("Account created. You can sign in now.");
  await page.getByRole("link", { name: "Sign in" }).last().click();
  await signIn(page, name);
  await expect(page).toHaveURL(/\/account$/);
});

test("a registration that needs verification says to check email, cannot sign in, then verifies from the link @smoke", async ({ page }) => {
  const name = accountName("unverified");
  await fillRegistration(page, name);
  await page.getByTestId("register-submit").click();
  await expect(page.getByTestId("register-done")).toContainText("Check your email");

  await page.goto("/login");
  await signIn(page, name);
  await expect(page.getByTestId("login-error")).toContainText("Verify your email address first");

  await page.goto(`/verify-email?token=vt-${name}&email=${encodeURIComponent(emailFor(name))}`);
  await expect(page.getByTestId("verify-status")).toContainText("verified");
  expect(page.url()).not.toContain("token="); // the single-use token does not stay in the address bar

  await page.goto("/login?redirect=%2Faccount");
  await signIn(page, name);
  await expect(page).toHaveURL(/\/account$/);
});

test("a bad verification link says so and offers a new one without revealing whether the address exists @smoke", async ({ page }) => {
  await page.goto(`/verify-email?token=nope&email=${encodeURIComponent(emailFor("nobody"))}`);
  await expect(page.getByTestId("verify-status")).toContainText("invalid or has expired");

  await page.getByTestId("resend-email").fill(emailFor("nobody"));
  await page.getByTestId("resend-submit").click();
  await expect(page.getByTestId("resend-status")).toHaveText("If that address needs verification, a new link has been sent.");
});

test("registration needs both consents before it can be submitted @smoke", async ({ page }) => {
  await fillRegistration(page, accountName("reg"), "short");
  await page.getByTestId("register-privacy").uncheck();
  await expect(page.getByTestId("register-submit")).toBeDisabled();
  await page.getByTestId("register-privacy").check();
  await expect(page.getByTestId("register-submit")).toBeEnabled();
});
