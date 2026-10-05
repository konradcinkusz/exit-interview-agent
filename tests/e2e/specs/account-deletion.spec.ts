import { expect, test } from "@playwright/test";
import { FAKE_PASSWORD, emailFor } from "../support/constants.mjs";
import { accountName, signIn, stats } from "./support";

// Account deletion semantics (ADR-0013): deleting the account removes the login; it cannot delete submissions, because
// records are not linked to accounts. The page must say so before and after.

test("the account page says what deletion does and does not do before the user commits @smoke", async ({ page }) => {
  await page.goto("/login?redirect=%2Faccount");
  await signIn(page, accountName("delete"));

  const facts = page.getByTestId("deletion-facts");
  await expect(facts).toContainText("removes your login");
  await expect(facts).toContainText("does not delete anything you submitted");
  await expect(facts).toContainText("not linked to your account");
  await expect(facts).toContainText("receipt code");
});

test("a wrong password or a missing confirmation deletes nothing @smoke", async ({ page, request }) => {
  const name = accountName("delete");
  await page.goto("/login?redirect=%2Faccount");
  await signIn(page, name);

  await page.getByTestId("delete-password").fill("not-the-password");
  await page.getByTestId("delete-confirmation").fill("DELETE");
  await page.getByTestId("delete-submit").click();
  await expect(page.getByTestId("delete-error")).toHaveText("That password is not correct.");

  await page.getByTestId("delete-password").fill(FAKE_PASSWORD);
  await page.getByTestId("delete-confirmation").fill("delete");
  await page.getByTestId("delete-submit").click();
  await expect(page.getByTestId("delete-error")).toHaveText("Type DELETE exactly to confirm.");

  expect((await stats(request, name)).deleted).toBe(false);
  await expect(page).toHaveURL(/\/account$/);
});

test("deleting the account ends the session, closes the login and repeats that submissions are untouched @smoke", async ({ page, context, request }) => {
  const name = accountName("delete");
  await page.goto("/login?redirect=%2Faccount");
  await signIn(page, name);

  await page.getByTestId("delete-password").fill(FAKE_PASSWORD);
  await page.getByTestId("delete-confirmation").fill("DELETE");
  await page.getByTestId("delete-submit").click();

  await expect(page).toHaveURL(/\/account-deleted$/);
  await expect(page.getByTestId("deleted-not-submissions")).toContainText("does not delete anything you submitted");
  await expect(page.getByTestId("deleted-receipt")).toContainText("receipt code");
  expect((await context.cookies()).filter((c) => c.name.startsWith("eia_"))).toEqual([]);
  expect((await stats(request, name)).deleted).toBe(true);

  await page.goto("/login");
  await page.getByTestId("login-email").fill(emailFor(name));
  await page.getByTestId("login-password").fill(FAKE_PASSWORD);
  await page.getByTestId("login-submit").click();
  await expect(page.getByTestId("login-error")).toHaveText("Invalid email or password.");
});
