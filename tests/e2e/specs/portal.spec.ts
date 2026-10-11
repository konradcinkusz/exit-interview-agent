import { expect, test } from "@playwright/test";
import { accountName, signIn } from "./support";

// The portal around the interview, as a former employee meets it (docs/ux/UI-UX.md, "Navigation" and "Language"): one primary
// action on the start page, a short header, the developer pages behind the footer, the build notice once, and the whole path in
// Polish or English, chosen by the browser and switchable.

test("signed out, the start page has one primary action: start the interview, through sign-in @smoke", async ({ page }) => {
  await page.goto("/");
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Give your exit interview to an AI");
  const start = page.getByTestId("interview-link");
  await expect(start).toHaveText("Start your interview");
  await expect(start).toHaveAttribute("href", "/login?redirect=%2Finterview");
  await expect(page.getByTestId("home-kept")).toContainText("Anthropic");
  await expect(page.getByTestId("home-kept")).toContainText("30 minutes");

  await start.click();
  await expect(page).toHaveURL(/\/login\?redirect=%2Finterview$/);
  await expect(page.getByText("Sign in to start your exit interview.")).toBeVisible();
  await signIn(page, accountName("home"));
  await expect(page).toHaveURL(/\/interview$/);
});

test("signed in, the start page's button opens the interview, and the header shows the account and sign-out @smoke", async ({ page }) => {
  await page.goto("/login?redirect=%2F");
  await signIn(page, accountName("home-in"));
  await expect(page).toHaveURL(/\/$/);
  await expect(page.getByTestId("interview-link")).toHaveAttribute("href", "/interview");
  const header = page.getByRole("navigation", { name: "Main" });
  await expect(header.getByRole("link", { name: "Account" })).toBeVisible();
  await expect(header.getByRole("link", { name: "Sign in" })).toHaveCount(0);

  await header.getByRole("button", { name: "Sign out" }).click();
  await expect(header.getByRole("link", { name: "Sign in" })).toBeVisible();
  await page.goto("/account");
  await expect(page).toHaveURL(/\/login\?redirect=%2Faccount$/);
});

test("the development notice appears once, in the footer, and never says the site is not for real people @smoke", async ({ page }) => {
  for (const path of ["/", "/login", "/privacy", "/about"]) {
    await page.goto(path);
    await expect(page.getByText(/Development build/)).toHaveCount(1);
    await expect(page.getByRole("contentinfo").getByTestId("dev-notice")).toBeVisible();
    await expect(page.locator("body")).not.toContainText(/not for real people/i);
  }
});

test("every developer page still works and is reached from the footer through For developers @smoke", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("contentinfo").getByRole("link", { name: "For developers" }).click();
  await expect(page).toHaveURL(/\/developers$/);
  const links = page.getByTestId("developer-links").getByRole("link");
  await expect(links).toHaveText(["Connect your AI client", "Use the command line", "Delete a submission", "Employer signals"]);
  await links.filter({ hasText: "Delete a submission" }).click();
  await expect(page).toHaveURL(/\/delete-submission$/);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Delete a submission");
  await page.goto("/developers");
  await links.filter({ hasText: "Connect your AI client" }).click();
  await expect(page.getByTestId("mcp-url")).toBeVisible();
});

test.describe("a Polish browser", () => {
  test.use({ locale: "pl-PL" });

  test("gets the portal in Polish by default, with lang=pl, and every page on the path is translated @smoke", async ({ page }) => {
    await page.goto("/");
    await expect(page.locator("html")).toHaveAttribute("lang", "pl");
    await expect(page.getByRole("heading", { level: 1 })).toHaveText("Przeprowadź rozmowę wyjściową z AI");
    await expect(page.getByTestId("interview-link")).toHaveText("Rozpocznij rozmowę");
    await expect(page.getByRole("link", { name: "Przejdź do treści" })).toBeAttached();
    await expect(page.getByTestId("dev-notice")).toContainText("Wersja deweloperska");

    const pages: Array<[string, string]> = [
      ["/login", "Zaloguj się"],
      ["/register", "Załóż konto"],
      ["/privacy", "Prywatność w skrócie"],
      ["/about", "O projekcie"],
      ["/developers", "Dla programistów"],
      ["/verify-email", "Potwierdź adres e-mail"],
      ["/account-deleted", "Twoje konto jest zamknięte"],
    ];
    for (const [path, heading] of pages) {
      await page.goto(path);
      await expect(page.getByRole("heading", { level: 1 }), path).toHaveText(heading);
    }

    await page.goto("/login");
    await signIn(page, accountName("pl"), "wrong-password");
    await expect(page.getByTestId("login-error")).toHaveText("Nieprawidłowy e-mail lub hasło.");
  });

  test("the account page and the consent step are in Polish @smoke", async ({ page }) => {
    await page.goto("/login?redirect=%2Faccount");
    await signIn(page, accountName("consent-pl"));
    await expect(page.getByRole("heading", { level: 1 })).toHaveText("Zanim przejdziesz dalej");
    await page.getByTestId("consent-terms").check();
    await page.getByTestId("consent-privacy").check();
    await page.getByTestId("consent-accept").click();
    await expect(page).toHaveURL(/\/account$/);
    await expect(page.getByRole("heading", { level: 1 })).toHaveText("Moje konto");
    await expect(page.getByTestId("deletion-facts")).toContainText("Nie usuwa niczego, co przesłałeś/przesłałaś");
  });

  test("a developer page says, in Polish, that it is in English, and marks its content as English @smoke", async ({ page }) => {
    await page.goto("/connect");
    await expect(page.getByTestId("english-only")).toHaveText("Ta strona jest dostępna tylko po angielsku.");
    await expect(page.locator('main [lang="en"]').getByRole("heading", { level: 1 })).toHaveText("Connect your AI client");
  });
});

test("the language switch changes the page in place, is remembered in a cookie, and wins over the browser @smoke", async ({ page, context }) => {
  await page.goto("/privacy");
  await expect(page.locator("html")).toHaveAttribute("lang", "en");
  await page.getByTestId("language-switch").click();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Prywatność w skrócie");
  await expect(page.locator("html")).toHaveAttribute("lang", "pl");
  await expect(page).toHaveURL(/\/privacy$/);

  const cookie = (await context.cookies()).find((c) => c.name === "locale");
  expect(cookie?.value).toBe("pl");
  expect(cookie?.sameSite).toBe("Lax");
  // The only cookie a page can read is the language: the session cookies stay HttpOnly.
  expect(await page.evaluate(() => document.cookie)).toBe("locale=pl");

  await page.goto("/");
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Przeprowadź rozmowę wyjściową z AI");
  await page.getByTestId("language-switch").click();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Give your exit interview to an AI");
  expect((await context.cookies()).find((c) => c.name === "locale")?.value).toBe("en");
});
