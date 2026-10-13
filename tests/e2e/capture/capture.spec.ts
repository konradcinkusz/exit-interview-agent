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
  /** Viewports other than the default desktop and mobile pair (for example 200% zoom: half the CSS width at scale 2). */
  viewports?: readonly { name: string; width: number; height: number; scale?: number }[];
  /** Skip the state when the page ends somewhere else (the route does not exist on this build). */
  expectPath?: string;
  /** Capture while a request is still open (the typing state): do not wait for the network to go idle. */
  busy?: boolean;
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

/** The 1.3 dynamics script (stub switch `dynamics`): consent, then `answers` replies. 1: warm-up, 2: the 1-5 rating, 3: check-in, 4: support, 5: legal. */
async function toDynamics(page: Page, request: APIRequestContext, answers: number) {
  await interview(page, request, "credits=1&dynamics=1");
  await toChat(page);
  const pl = (await page.locator("html").getAttribute("lang")) === "pl";
  const replies = pl
    ? ["Tak, per Pan", "Byłem programistą w zespole płatności.", "2", "Brak planu i odwoływane spotkania.", "Było mi naprawdę ciężko."]
    : ["Yes", "I was a developer in the payments team.", "2", "No plan, and cancelled meetings.", "It was really hard for me."];
  for (const text of replies.slice(0, answers)) await answer(page, text);
}

const LONG_ANSWER = {
  en: "Honestly, onboarding was chaotic. There was no written plan, my laptop arrived in the second week, and the person who was supposed to show me around left a month after I joined. I learned most things by asking colleagues between their own tasks. They were patient, but it felt like everyone was improvising, and nobody owned the process.",
  pl: "Szczerze mówiąc, wdrożenie było chaotyczne. Nie było spisanego planu, laptop dotarł w drugim tygodniu, a osoba, która miała mnie wprowadzić, odeszła miesiąc po moim przyjściu. Większości rzeczy uczyłem się, pytając kolegów między ich zadaniami. Byli cierpliwi, ale miałem wrażenie, że wszyscy improwizują i nikt nie odpowiada za ten proces.",
};

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
  // the 1.3 dynamics and the chat's states (2026-10-13)
  {
    name: "interview-chat-quick",
    lang: "en",
    run: async (p, r) => {
      await interview(p, r, "credits=1");
      await toChat(p);
    },
  },
  {
    name: "interview-chat-quick-pl",
    lang: "pl",
    run: async (p, r) => {
      await interview(p, r, "credits=1");
      await toChat(p);
    },
  },
  { name: "interview-chat-warmup", lang: "en", run: async (p, r) => toDynamics(p, r, 1) },
  { name: "interview-chat-rating", lang: "en", run: async (p, r) => toDynamics(p, r, 2) },
  { name: "interview-chat-rating-pl", lang: "pl", run: async (p, r) => toDynamics(p, r, 2) },
  { name: "interview-chat-checkin", lang: "en", run: async (p, r) => toDynamics(p, r, 3) },
  { name: "interview-chat-support", lang: "en", run: async (p, r) => toDynamics(p, r, 4) },
  { name: "interview-chat-support-pl", lang: "pl", run: async (p, r) => toDynamics(p, r, 4) },
  { name: "interview-chat-legal", lang: "en", run: async (p, r) => toDynamics(p, r, 5) },
  {
    name: "interview-chat-long",
    lang: "en",
    run: async (p, r) => {
      await interview(p, r, "credits=1");
      await toChat(p);
      await answer(p, "Yes");
      await answer(p, LONG_ANSWER.en);
    },
  },
  {
    name: "interview-chat-long-pl",
    lang: "pl",
    run: async (p, r) => {
      await interview(p, r, "credits=1");
      await toChat(p);
      await answer(p, "Tak");
      await answer(p, LONG_ANSWER.pl);
    },
  },
  {
    name: "interview-chat-typing",
    lang: "en",
    busy: true,
    run: async (p, r) => {
      await interview(p, r, "credits=1");
      await toChat(p);
      // Hold the reply so the waiting state can be seen (capture only).
      await p.route("**/reply", () => undefined);
      const box = p.locator("textarea");
      await box.fill("Onboarding was chaotic.");
      await box.press("Enter");
      await p.waitForTimeout(500);
    },
  },
  {
    name: "interview-result-long",
    lang: "en",
    run: async (p, r) => {
      await interview(p, r, "credits=1&longDrafts=1");
      await toResult(p);
    },
  },
  {
    name: "interview-result-long-pl",
    lang: "pl",
    run: async (p, r) => {
      await interview(p, r, "credits=1&longDrafts=1");
      await toResult(p);
    },
  },
  {
    name: "interview-result-copied",
    lang: "en",
    run: async (p, r) => {
      await interview(p, r, "credits=1");
      await toResult(p);
      const card = p.locator("article").filter({ hasText: "LinkedIn" });
      await card.getByRole("checkbox").check();
      await card.getByRole("button", { name: /Copy/ }).first().click();
      await p.locator("article").filter({ hasText: "Glassdoor" }).getByRole("button", { name: /Copy/ }).first().click();
      await p.waitForTimeout(200);
    },
  },
  {
    name: "interview-result-loading",
    lang: "en",
    busy: true,
    run: async (p, r) => {
      await interview(p, r, "credits=1");
      await p.route("**/result", () => undefined);
      await toResult(p);
    },
  },
  {
    name: "interview-result-failed",
    lang: "en",
    run: async (p, r) => {
      await interview(p, r, "credits=1");
      await p.route("**/result", (route) => route.fulfill({ status: 503, contentType: "application/problem+json", body: JSON.stringify({ code: "unavailable" }) }));
      await toResult(p);
    },
  },
  {
    name: "interview-stopped",
    lang: "en",
    run: async (p, r) => {
      await interview(p, r, "credits=1");
      await toChat(p);
      await answer(p, "stop");
      await p.waitForTimeout(1500);
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
  // accessibility evidence (phase 3): the keyboard focus, and 200% zoom of a 1280 px window
  {
    name: "focus-start-continue",
    lang: "en",
    viewports: [{ name: "desktop", width: 1280, height: 800 }],
    run: async (p, r) => {
      await interview(p, r, "credits=1");
      await p.locator("main button").filter({ hasText: "Continue" }).focus();
      await p.keyboard.press("Shift+Tab");
      await p.keyboard.press("Tab");
    },
  },
  {
    name: "focus-choice-row",
    lang: "pl",
    viewports: [{ name: "desktop", width: 1280, height: 800 }],
    run: async (p, r) => {
      await interview(p, r, "credits=1");
      await p.locator("#tenure").focus();
      await p.keyboard.press("Shift+Tab");
    },
  },
  {
    name: "focus-dialog-cancel",
    lang: "en",
    viewports: [{ name: "desktop", width: 1280, height: 800 }],
    run: async (p, r) => {
      await interview(p, r, "credits=1");
      await toChat(p);
      await p.locator("main button").filter({ hasText: "Stop and delete" }).focus();
      await p.keyboard.press("Enter");
      await p.keyboard.press("Tab");
      await p.keyboard.press("Shift+Tab");
    },
  },
  ...(["interview-start", "interview-chat", "interview-result"] as const).map((base) => ({
    name: `zoom200-${base}-pl`,
    lang: "pl" as const,
    viewports: [{ name: "desktop", width: 640, height: 400, scale: 2 }],
    run: async (p: Page, r: APIRequestContext) => {
      await interview(p, r, "credits=1");
      if (base === "interview-chat") {
        await toChat(p);
        await answer(p, "Wdrożenie było chaotyczne.");
      }
      if (base === "interview-result") await toResult(p);
    },
  })),
];

for (const state of STATES) {
  for (const viewport of state.viewports ?? VIEWPORTS) {
    test(`${state.name} ${viewport.name}`, async ({ browser, baseURL, request }) => {
      const context = await browser.newContext({
        viewport: { width: viewport.width, height: viewport.height },
        deviceScaleFactor: "scale" in viewport ? viewport.scale : 1,
        locale: state.lang === "pl" ? "pl-PL" : "en-GB",
        colorScheme: process.env.CAPTURE_SCHEME === "dark" ? "dark" : "light",
        reducedMotion: "reduce", // stable captures: no animation or smooth scroll caught mid-way
        permissions: ["clipboard-read", "clipboard-write"],
      });
      if (state.lang === "pl") await context.addCookies([{ name: "locale", value: "pl", url: baseURL! }]);
      const page = await context.newPage();
      await state.run(page, request);
      test.skip(Boolean(state.expectPath) && new URL(page.url()).pathname !== state.expectPath, `${state.name} does not exist on this build`);
      if (!state.busy) await page.waitForLoadState("networkidle");
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
