import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";
import { STUB_URL, emailFor } from "../support/constants.mjs";
import { accountName, signIn } from "./support";

// Automated accessibility check (axe-core, WCAG 2.0/2.1 level A and AA rules) on every page, in light and dark, plus the
// states a page reaches after an action. axe finds a subset of problems (roughly the machine-checkable ones); it does not
// replace keyboard and screen-reader testing by a person (docs/ux/UI-UX.md).

const TAGS = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"];

async function expectNoViolations(page: Page, label: string) {
  const results = await new AxeBuilder({ page }).withTags(TAGS).analyze();
  const summary = results.violations.map((v) => `${v.id} (${v.impact}): ${v.nodes.map((n) => n.target.join(" ")).join(" | ")}`);
  expect(summary, `${label}: axe violations`).toEqual([]);
  expect(results.passes.length, `${label}: axe actually ran rules`).toBeGreaterThan(5);
}

for (const scheme of ["light", "dark"] as const) {
  test.describe(`${scheme} colour scheme`, () => {
    test.use({ colorScheme: scheme });

    for (const path of ["/", "/login", "/register", "/verify-email", "/connect", "/privacy", "/delete-submission", "/account-deleted"]) {
      test(`${path} has no axe violations @smoke`, async ({ page }) => {
        await page.goto(path);
        await page.waitForLoadState("networkidle");
        await expectNoViolations(page, `${scheme} ${path}`);
      });
    }

    test("the signed-in pages have no axe violations @smoke", async ({ page }) => {
      await page.goto("/login?redirect=%2Faccount");
      await signIn(page, accountName("a11y"));
      await expect(page.getByTestId("account-subject")).toBeVisible();
      await expectNoViolations(page, `${scheme} /account`);

      await page.goto("/cli");
      await expectNoViolations(page, `${scheme} /cli`);

      await page.goto("/does-not-exist"); // signed in, so the gate lets the unknown path through to the 404 page
      await expect(page.getByTestId("not-found")).toBeVisible();
      await expectNoViolations(page, `${scheme} 404`);
    });

    test("the pages' action states have no axe violations @smoke", async ({ page, request }) => {
      // login error and the second-factor step
      await page.goto("/login");
      await signIn(page, accountName("a11y"), "wrong-password");
      await expect(page.getByTestId("login-error")).toBeVisible();
      await expectNoViolations(page, `${scheme} login error`);

      await signIn(page, accountName("twofactor"));
      await expect(page.getByTestId("twofactor-form")).toBeVisible();
      await expectNoViolations(page, `${scheme} second factor`);

      // receipt result and error
      await page.goto("/delete-submission");
      await page.getByTestId("receipt-input").fill("A".repeat(46));
      await page.getByTestId("receipt-submit").click();
      await expect(page.getByTestId("receipt-error")).toBeVisible();
      await expectNoViolations(page, `${scheme} receipt error`);
      const { code } = (await (await request.get(`${STUB_URL}/__test/receipt-code`)).json()) as { code: string };
      await page.getByTestId("receipt-input").fill(code);
      await page.getByTestId("receipt-submit").click();
      await expect(page.getByTestId("receipt-result")).toBeVisible();
      await expectNoViolations(page, `${scheme} receipt result`);

      // ticket shown
      await page.goto("/login?redirect=%2Fcli");
      await signIn(page, accountName("a11y"));
      await page.getByTestId("ticket-create").click();
      await expect(page.getByTestId("ticket-value")).toBeVisible();
      await expectNoViolations(page, `${scheme} ticket shown`);
    });

    test("the consent step has no axe violations @smoke", async ({ page }) => {
      await page.goto("/login");
      await signIn(page, accountName("consent"));
      await expect(page.getByTestId("consent-accept")).toBeVisible();
      await expectNoViolations(page, `${scheme} consent`);
    });
  });
}

test("every page can be used by keyboard alone: the skip link is the first stop and a form can be submitted @smoke", async ({ page }) => {
  await page.goto("/login");
  await page.keyboard.press("Tab");
  await expect(page.getByRole("link", { name: "Skip to content" })).toBeFocused();
  await page.keyboard.press("Enter");
  await expect(page.locator("main")).toBeFocused();

  const name = accountName("keyboard");
  await page.getByTestId("login-email").focus();
  await page.keyboard.type(emailFor(name));
  await page.keyboard.press("Tab");
  await page.keyboard.type("dev-only-e2e-password");
  await page.keyboard.press("Enter");
  await expect(page).toHaveURL(/\/account$/);
});

test("the layout holds at 320 px wide (the width of 400% zoom): nothing scrolls sideways @smoke", async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 640 });
  for (const path of ["/", "/login", "/connect", "/delete-submission", "/privacy", "/register"]) {
    await page.goto(path);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow, `${path} horizontal overflow at 320px`).toBeLessThanOrEqual(0);
  }
});

test("a page announces errors in an alert and every field has a label @smoke", async ({ page }) => {
  await page.goto("/login");
  await signIn(page, accountName("labels"), "nope");
  await expect(page.getByRole("alert").filter({ hasText: "Invalid email or password." })).toBeVisible();
  await expect(page.getByLabel("Email")).toBeVisible();
  await expect(page.getByLabel("Password")).toBeVisible();
});
