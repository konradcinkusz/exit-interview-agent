import { mkdirSync } from "node:fs";
import { join } from "node:path";
import { expect, test, type APIRequestContext, type Page } from "@playwright/test";
import { STUB_URL, emailFor } from "../support/constants.mjs";
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
  run: (page: Page, request: APIRequestContext) => Promise<void>;
}

/** Signs a fresh account in on /interview with `config` applied to the stub (credits, failure switches). Returns the account. */
async function interview(page: Page, request: APIRequestContext, config: string): Promise<string> {
  const name = accountName("cap-iv");
  await request.post(`${STUB_URL}/__test/interview-config?email=${encodeURIComponent(emailFor(name))}&${config}`);
  await page.goto("/login?redirect=%2Finterview");
  await signIn(page, name);
  await page.waitForURL((url) => url.pathname === "/interview");
  await page.locator("#credit-status, [role=status]").first().waitFor();
  await page.waitForTimeout(600); // the credit line settles (capture only, not a test)
  return name;
}

const configure = (request: APIRequestContext, name: string, config: string) =>
  request.post(`${STUB_URL}/__test/interview-config?email=${encodeURIComponent(emailFor(name))}&${config}`);

async function toChat(page: Page) {
  await page.locator("main button").filter({ hasText: /^(Continue|Dalej)$/ }).click();
  await page.getByRole("checkbox").check();
  await page.locator("main button").filter({ hasText: /^(Start the interview|Rozpocznij rozmowę)$/ }).click();
  await page.getByRole("log").waitFor();
  await page.waitForTimeout(300);
}

async function answer(page: Page, text: string) {
  const box = page.locator("textarea");
  await box.fill(text);
  await box.press("Enter");
  await page.waitForTimeout(700);
}

async function toResult(page: Page) {
  await toChat(page);
  await answer(page, "Onboarding was chaotic.");
  await answer(page, "My manager was rarely available.");
  await answer(page, "One meeting was cancelled three times.");
  await page.waitForTimeout(1500);
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
  // the interview page (phase 2)
  { name: "interview-no-credit", lang: "en", run: async (p, r) => void (await interview(p, r, "credits=0")) },
  { name: "interview-start", lang: "en", run: async (p, r) => void (await interview(p, r, "credits=1")) },
  {
    name: "interview-consent",
    lang: "en",
    run: async (p, r) => {
      await interview(p, r, "credits=1");
      await p.locator("main button").filter({ hasText: "Continue" }).click();
    },
  },
  {
    name: "interview-402",
    lang: "en",
    run: async (p, r) => {
      const name = await interview(p, r, "credits=1");
      await p.locator("main button").filter({ hasText: "Continue" }).click();
      await configure(r, name, "credits=0");
      await p.getByRole("checkbox").check();
      await p.locator("main button").filter({ hasText: "Start the interview" }).click();
      await p.waitForTimeout(800);
    },
  },
  {
    name: "interview-chat",
    lang: "en",
    run: async (p, r) => {
      await interview(p, r, "credits=1");
      await toChat(p);
      await answer(p, "Onboarding was chaotic.");
    },
  },
  {
    name: "interview-chat-429",
    lang: "en",
    run: async (p, r) => {
      const name = await interview(p, r, "credits=1");
      await toChat(p);
      await configure(r, name, "replyRateLimited=1");
      await answer(p, "My answer, kept.");
    },
  },
  {
    name: "interview-chat-503",
    lang: "en",
    run: async (p, r) => {
      const name = await interview(p, r, "credits=1");
      await toChat(p);
      await configure(r, name, "providerDown=1");
      await answer(p, "My answer, kept.");
    },
  },
  {
    name: "interview-unverified-403",
    lang: "en",
    run: async (p, r) => {
      await interview(p, r, "credits=1&emailUnverified=1");
      await p.locator("main button").filter({ hasText: "Continue" }).click();
      await p.getByRole("checkbox").check();
      await p.locator("main button").filter({ hasText: "Start the interview" }).click();
      await p.getByRole("alert").waitFor();
    },
  },
  {
    name: "interview-delete-dialog",
    lang: "en",
    run: async (p, r) => {
      await interview(p, r, "credits=1");
      await toChat(p);
      await p.locator("main button").filter({ hasText: "Stop and delete" }).click();
      await p.waitForTimeout(300);
    },
  },
  { name: "interview-result", lang: "en", run: async (p, r) => { await interview(p, r, "credits=1"); await toResult(p); } },
  {
    name: "interview-deleted",
    lang: "en",
    run: async (p, r) => {
      await interview(p, r, "credits=1");
      await toResult(p);
      await p.locator("main button").filter({ hasText: "Delete everything now" }).click();
      await p.getByRole("dialog").locator("button").filter({ hasText: "Yes, delete it" }).click();
      await p.waitForTimeout(800);
    },
  },
  { name: "interview-start-pl", lang: "pl", run: async (p, r) => void (await interview(p, r, "credits=1")) },
  {
    name: "interview-chat-pl",
    lang: "pl",
    run: async (p, r) => {
      await interview(p, r, "credits=1");
      await toChat(p);
      await answer(p, "Wdrożenie było chaotyczne.");
    },
  },
  { name: "interview-result-pl", lang: "pl", run: async (p, r) => { await interview(p, r, "credits=1"); await toResult(p); } },
];

for (const state of STATES) {
  for (const viewport of VIEWPORTS) {
    test(`${state.name} ${viewport.name}`, async ({ browser, baseURL, request }) => {
      const context = await browser.newContext({
        viewport: { width: viewport.width, height: viewport.height },
        locale: state.lang === "pl" ? "pl-PL" : "en-GB",
        colorScheme: process.env.CAPTURE_SCHEME === "dark" ? "dark" : "light",
        reducedMotion: "reduce", // stable captures: no animation or smooth scroll caught mid-way
      });
      if (state.lang === "pl") await context.addCookies([{ name: "locale", value: "pl", url: baseURL! }]);
      const page = await context.newPage();
      await state.run(page, request);
      test.skip(Boolean(state.expectPath) && new URL(page.url()).pathname !== state.expectPath, `${state.name} does not exist on this build`);
      await page.waitForLoadState("networkidle");
      await expect(page.locator("main")).toBeVisible();
      // A full-page capture draws sticky elements where the page happens to be scrolled; lay them out in place for the picture.
      // (Through the CSSOM: the CSP refuses injected style elements.)
      await page.evaluate(() => document.querySelectorAll<HTMLElement>(".site-header, form").forEach((el) => el.style.setProperty("position", "static", "important")));
      mkdirSync(OUT, { recursive: true });
      await page.screenshot({ path: join(OUT, `${state.name}-${viewport.name}.png`), fullPage: true });
      await context.close();
    });
  }
}
