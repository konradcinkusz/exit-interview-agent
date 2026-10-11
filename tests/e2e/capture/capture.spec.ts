import { mkdirSync } from "node:fs";
import { join } from "node:path";
import { expect, test, type Page } from "@playwright/test";
import { accountName, signIn } from "../specs/support";

// Full-page screenshots of the states a reviewer needs to see, at desktop and mobile size. Not assertions: a state that
// cannot be reached on the build being captured is skipped with a note. Each state is a test, so `-g` picks some.

const OUT = process.env.CAPTURE_DIR ?? "capture-out";
const VIEWPORTS = [
  { name: "desktop", width: 1280, height: 800 },
  { name: "mobile", width: 390, height: 844 },
] as const;

type Lang = "en" | "pl";
interface State {
  name: string;
  lang: Lang;
  /** Skip the state when the page ends somewhere else (the route does not exist on this build). */
  expectPath?: string;
  run: (page: Page) => Promise<void>;
}

async function signedIn(page: Page, prefix: string, redirect = "/account") {
  await page.goto(`/login?redirect=${encodeURIComponent(redirect)}`);
  await signIn(page, accountName(prefix));
  await page.waitForURL((url) => url.pathname === redirect);
}

const STATES: State[] = [
  { name: "home-signed-out", lang: "en", run: async (p) => void (await p.goto("/")) },
  { name: "home-signed-in", lang: "en", run: async (p) => { await signedIn(p, "cap"); await p.goto("/"); } },
  { name: "login", lang: "en", run: async (p) => void (await p.goto("/login?redirect=%2Finterview")) },
  { name: "register", lang: "en", run: async (p) => { await p.goto("/register"); await p.getByTestId("register-terms").waitFor(); } },
  {
    name: "consent",
    lang: "en",
    run: async (p) => {
      await p.goto("/login?redirect=%2Finterview");
      await signIn(p, accountName("consent-cap"));
      await p.getByTestId("consent-accept").waitFor();
    },
  },
  { name: "account", lang: "en", run: async (p) => { await signedIn(p, "cap"); await p.getByTestId("account-subject").waitFor(); } },
  { name: "privacy", lang: "en", run: async (p) => void (await p.goto("/privacy")) },
  { name: "developers", lang: "en", expectPath: "/developers", run: async (p) => void (await p.goto("/developers")) },
  { name: "about", lang: "en", expectPath: "/about", run: async (p) => void (await p.goto("/about")) },
  { name: "not-found", lang: "en", run: async (p) => { await signedIn(p, "cap"); await p.goto("/no-such-page"); } },
  { name: "home-signed-out-pl", lang: "pl", run: async (p) => void (await p.goto("/")) },
  { name: "home-signed-in-pl", lang: "pl", run: async (p) => { await signedIn(p, "cap"); await p.goto("/"); } },
  { name: "login-pl", lang: "pl", run: async (p) => void (await p.goto("/login?redirect=%2Finterview")) },
  { name: "register-pl", lang: "pl", run: async (p) => { await p.goto("/register"); await p.getByTestId("register-terms").waitFor(); } },
  { name: "account-pl", lang: "pl", run: async (p) => { await signedIn(p, "cap"); await p.getByTestId("account-subject").waitFor(); } },
];

for (const state of STATES) {
  for (const viewport of VIEWPORTS) {
    test(`${state.name} ${viewport.name}`, async ({ browser, baseURL }) => {
      const context = await browser.newContext({
        viewport: { width: viewport.width, height: viewport.height },
        locale: state.lang === "pl" ? "pl-PL" : "en-GB",
        colorScheme: "light",
      });
      if (state.lang === "pl") await context.addCookies([{ name: "locale", value: "pl", url: baseURL! }]);
      const page = await context.newPage();
      await state.run(page);
      test.skip(Boolean(state.expectPath) && new URL(page.url()).pathname !== state.expectPath, `${state.name} does not exist on this build`);
      await page.waitForLoadState("networkidle");
      await expect(page.locator("main")).toBeVisible();
      mkdirSync(OUT, { recursive: true });
      await page.screenshot({ path: join(OUT, `${state.name}-${viewport.name}.png`), fullPage: true });
      await context.close();
    });
  }
}
