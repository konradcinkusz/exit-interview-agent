import AxeBuilder from "@axe-core/playwright";
import { expect, test, type APIRequestContext, type Page } from "@playwright/test";
import { STUB_URL, emailFor } from "../support/constants.mjs";
import { accountName, hydrated, signIn } from "./support";

// Accessibility of every interview state the stub can reach (redesign phase 3), in both colour schemes and both languages: axe
// (WCAG 2.0/2.1 A and AA, which includes text contrast), the contrast of control borders and focus rings (WCAG 1.4.11, which axe
// does not measure), a visible focus on every stop, the keyboard order, reduced motion, 200% zoom and 390 px without sideways
// scrolling. axe and these checks find the machine-checkable subset; a person with a screen reader is still owed (OP-16).

const TAGS = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"];

const configure = (request: APIRequestContext, name: string, query: string) =>
  request.post(`${STUB_URL}/__test/interview-config?email=${encodeURIComponent(emailFor(name))}&${query}`);

const T = {
  en: { cont: "Continue", start: "Start the interview", answer: "Your answer", stop: "Stop and delete", delAll: "Delete everything now", yes: "Yes, delete it", terms: "Read the full terms", save: "Save a copy", log: "Conversation", typing: "The interviewer is typing", phase: "Interview · usually 10–15 minutes · you can finish at any time" },
  pl: { cont: "Dalej", start: "Rozpocznij rozmowę", answer: "Twoja odpowiedź", stop: "Przerwij i usuń", delAll: "Usuń wszystko teraz", yes: "Tak, usuń", terms: "Przeczytaj pełne warunki", save: "Zapisz kopię", log: "Rozmowa", typing: "Rozmówca pisze", phase: "Rozmowa · zwykle 10–15 minut · można zakończyć w każdej chwili" },
} as const;
type Lang = keyof typeof T;

async function axe(page: Page, label: string) {
  const results = await new AxeBuilder({ page }).withTags(TAGS).analyze();
  const summary = results.violations.map((v) => `${v.id} (${v.impact}): ${v.nodes.map((n) => n.target.join(" ")).join(" | ")}`);
  expect(summary, `${label}: axe violations`).toEqual([]);
  expect(results.passes.length, `${label}: axe actually ran rules`).toBeGreaterThan(5);
}


/**
 * WCAG 1.4.11 for what axe leaves out: the border of every text field, select and outlined button, and the focus ring, against
 * the background they sit on, at least 3:1. Native checkboxes and radios are drawn by the browser and are skipped.
 */
async function nonTextContrast(page: Page, label: string) {
  const failures = await page.evaluate(() => {
    const rgb = (c: string) => (c.match(/[\d.]+/g) ?? []).map(Number);
    const lum = ([r, g, b]: number[]) => {
      const f = (v: number) => ((v /= 255) <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4);
      return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b);
    };
    const ratio = (a: string, b: string) => {
      const [x, y] = [lum(rgb(a)), lum(rgb(b))].sort((m, n) => n - m);
      return (x + 0.05) / (y + 0.05);
    };
    const backgroundOf = (el: Element | null): string => {
      for (let e = el; e; e = e.parentElement) {
        const c = getComputedStyle(e).backgroundColor;
        const a = rgb(c);
        if (a.length === 3 || (a.length === 4 && a[3] > 0.9)) return c;
      }
      return getComputedStyle(document.body).backgroundColor;
    };
    const out: string[] = [];
    let checked = 0;
    const controls = document.querySelectorAll<HTMLElement>(
      'input[type="text"], input[type="email"], input[type="password"], select, textarea, button.secondary, button.danger-outline, .button.secondary, summary.button, .lang-switch',
    );
    for (const el of controls) {
      if (el.offsetParent === null && getComputedStyle(el).position !== "fixed") continue; // not rendered
      const style = getComputedStyle(el);
      if (style.opacity !== "1" || (el as HTMLButtonElement).disabled) continue; // disabled controls are exempt (1.4.11)
      checked++;
      const r = ratio(style.borderTopColor, backgroundOf(el.parentElement));
      if (r < 3) out.push(`border ${el.tagName.toLowerCase()}.${el.className} ${r.toFixed(2)}`);
    }
    const focus = getComputedStyle(document.documentElement).getPropertyValue("--focus").trim();
    const probe = document.createElement("div");
    probe.style.color = focus;
    document.body.append(probe);
    const focusRgb = getComputedStyle(probe).color;
    probe.remove();
    for (const surface of [document.body, document.querySelector("main"), document.querySelector(".card"), document.querySelector("dialog[open]")]) {
      if (!surface) continue;
      const r = ratio(focusRgb, backgroundOf(surface));
      if (r < 3) out.push(`focus ring on ${surface.tagName.toLowerCase()} ${r.toFixed(2)}`);
    }
    return { out, checked };
  });
  expect(failures.checked, `${label}: some controls were measured`).toBeGreaterThan(0);
  expect(failures.out, `${label}: non-text contrast below 3:1`).toEqual([]);
}

/** Tabs through the page (or the open dialog) and checks that every stop shows a focus indicator of at least 2 px. */
async function visibleFocus(page: Page, label: string, stops = 30) {
  const missing: string[] = [];
  const seen = new Set<string>();
  await page.locator("body").focus();
  for (let i = 0; i < stops; i++) {
    await page.keyboard.press("Tab");
    const info = await page.evaluate(() => {
      const el = document.activeElement as HTMLElement | null;
      if (!el || el === document.body) return null;
      // A checkbox or radio inside a choice row shows its ring on the row (globals.css, label.check:has(:focus-visible)).
      const target = el.matches("label.check input") ? (el.closest("label") as HTMLElement) : el;
      const s = getComputedStyle(target);
      const width = s.outlineStyle === "none" ? 0 : parseFloat(s.outlineWidth);
      const id = `${el.tagName.toLowerCase()}:${(el.textContent ?? el.getAttribute("aria-label") ?? el.id).trim().slice(0, 30)}`;
      return { id, width, shadow: s.boxShadow !== "none" };
    });
    if (!info) continue;
    if (seen.has(info.id)) break; // wrapped around
    seen.add(info.id);
    if (info.width < 2 && !info.shadow) missing.push(info.id);
  }
  expect(seen.size, `${label}: tabbed through some stops`).toBeGreaterThan(1);
  expect(missing, `${label}: focus stops without a visible indicator`).toEqual([]);
}

async function noSidewaysScroll(page: Page, label: string) {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow, `${label}: horizontal overflow`).toBeLessThanOrEqual(0);
}

/** Every interview state, in order, with a check run at each. Returns the labels it checked, so a test can assert it saw them all. */
async function walk(page: Page, request: APIRequestContext, lang: Lang, check: (label: string) => Promise<void>) {
  const t = T[lang];
  const name = accountName("a11y-iv");
  const open = async (config: string) => {
    await configure(request, name, config);
    await page.goto("/interview");
    await hydrated(page);
    await expect(page.getByTestId("credit-status")).not.toBeEmpty();
    await expect(page.getByTestId("credit-status")).not.toContainText(/Checking|Sprawdzam/);
  };
  const say = async (text: string) => {
    await expect(page.getByRole("status").filter({ hasText: t.typing })).toHaveCount(0);
    await page.getByLabel(t.answer).fill(text);
    await page.getByLabel(t.answer).press("Enter");
  };
  const seen: string[] = [];
  const at = async (label: string) => {
    seen.push(label);
    await check(label);
  };

  await page.goto("/login?redirect=%2Finterview");
  await signIn(page, name);

  await open("credits=0");
  await at("no credit");

  await open("credits=1");
  await at("start");
  await page.getByRole("button", { name: t.cont }).click();
  await page.getByText(t.terms).click();
  await at("consent, full terms open");

  await configure(request, name, "rateLimited=1");
  await page.getByRole("checkbox").check();
  await page.getByRole("button", { name: t.start }).click();
  await expect(page.getByTestId("interview-notice")).toBeVisible();
  await at("consent, 429 with countdown");
  await configure(request, name, "rateLimited=0&emailUnverified=1");
  await open("credits=1");
  await page.getByRole("button", { name: t.cont }).click();
  await page.getByRole("checkbox").check();
  await page.getByRole("button", { name: t.start }).click();
  await expect(page.getByTestId("interview-notice").getByRole("link")).toBeVisible();
  await at("consent, 403 unverified email");

  await configure(request, name, "emailUnverified=0");
  await open("credits=1");
  await page.getByRole("button", { name: t.cont }).click();
  await configure(request, name, "credits=0");
  await page.getByRole("checkbox").check();
  await page.getByRole("button", { name: t.start }).click();
  await expect(page.getByTestId("credit-status")).toContainText(/No interview|Brak/);
  await at("start, 402");

  await open("credits=1");
  await page.getByRole("button", { name: t.cont }).click();
  await page.getByRole("checkbox").check();
  await page.getByRole("button", { name: t.start }).click();
  await expect(page.getByRole("log", { name: t.log })).toBeVisible();
  await at("chat");
  await say("Onboarding was chaotic.");
  await expect(page.getByTestId("chat-progress")).toHaveText(t.phase);
  await at("chat, after the first answer");

  await configure(request, name, "replyRateLimited=1");
  await say("Kept through the wait.");
  await expect(page.getByTestId("wait-countdown")).toBeVisible();
  await at("chat, 429 with countdown");
  await configure(request, name, "replyRateLimited=0");
  // The rate-limited chat waits 30 s; a new interview is quicker. Delete this one (as the service would after a reload) and start again.
  const open429 = (await (await request.get(`${STUB_URL}/__test/interview?email=${encodeURIComponent(emailFor(name))}`)).json()) as {
    session: { id: string };
  };
  expect((await page.request.delete(`/api/proxy/v1/interviews/${open429.session.id}`)).status()).toBe(204);
  await open("credits=1");
  await page.getByRole("button", { name: t.cont }).click();
  await page.getByRole("checkbox").check();
  await page.getByRole("button", { name: t.start }).click();
  await expect(page.getByRole("log", { name: t.log })).toBeVisible();
  await configure(request, name, "providerDown=1");
  await say("The model is down.");
  await expect(page.getByTestId("interview-notice")).toBeVisible();
  await at("chat, 503 with the answer kept");
  await configure(request, name, "providerDown=0");

  await page.getByRole("button", { name: t.stop }).click();
  await expect(page.getByRole("dialog")).toBeVisible();
  await at("chat, delete dialog open");
  await page.getByRole("dialog").getByRole("button", { name: t.yes }).click();
  await expect(page.getByTestId("deleted-notice")).toBeVisible();
  await at("deleted");

  await open("credits=1");
  await page.getByRole("button", { name: t.cont }).click();
  await page.getByRole("checkbox").check();
  await page.getByRole("button", { name: t.start }).click();
  await expect(page.getByRole("log", { name: t.log })).toBeVisible();
  await say("Onboarding was chaotic.");
  await say("My manager was rarely available.");
  await say("One meeting was cancelled.");
  await expect(page.getByTestId("result-summary")).toBeVisible();
  await page.getByText(t.save).click();
  await at("result, save open");

  await page.getByRole("button", { name: t.delAll }).click();
  await expect(page.getByRole("dialog")).toBeVisible();
  await at("result, delete dialog open");
  await page.getByRole("dialog").getByRole("button", { name: t.yes }).click();
  await expect(page.getByTestId("deleted-notice")).toBeVisible();
  return seen;
}

for (const scheme of ["light", "dark"] as const) {
  for (const lang of ["en", "pl"] as const) {
    test.describe(`${scheme}, ${lang}`, () => {
      test.use({ colorScheme: scheme, locale: lang === "pl" ? "pl-PL" : "en-GB" });

      test(`every interview state has no axe violations @smoke`, async ({ page, request }) => {
        test.setTimeout(90_000);
        const seen = await walk(page, request, lang, (label) => axe(page, `${scheme} ${lang} ${label}`));
        expect(seen.length).toBeGreaterThanOrEqual(13);
      });
    });
  }
}

test("control borders and focus rings meet 3:1, and every tab stop shows its focus, in light and dark @smoke", async ({ browser, request }) => {
  test.setTimeout(120_000);
  for (const colorScheme of ["light", "dark"] as const) {
    const context = await browser.newContext({ colorScheme });
    const page = await context.newPage();
    for (const path of ["/", "/login", "/register", "/privacy", "/about", "/developers"]) {
      await page.goto(path);
      await hydrated(page);
      await nonTextContrast(page, `${colorScheme} ${path}`);
      await visibleFocus(page, `${colorScheme} ${path}`);
    }
    await walk(page, request, "en", async (label) => {
      await nonTextContrast(page, `${colorScheme} ${label}`);
      await visibleFocus(page, `${colorScheme} ${label}`);
    });
    await context.close();
  }
});

test("the interview's tab order: skip link, then the four header items, then the start form, in reading order @smoke", async ({ page, request }) => {
  const name = accountName("a11y-order");
  await configure(request, name, "credits=1");
  await page.goto("/login?redirect=%2Finterview");
  await signIn(page, name);
  await expect(page.getByTestId("credit-status")).toContainText("1 interview ready");
  await page.locator("body").focus();
  const order: string[] = [];
  for (let i = 0; i < 11; i++) {
    await page.keyboard.press("Tab");
    order.push(
      await page.evaluate(() => {
        const el = document.activeElement as HTMLElement;
        if (el.matches("input[type=radio]")) return `radio:${(el.closest("label")?.textContent ?? "").trim()}`;
        return el.tagName === "SELECT" ? "select:tenure" : el.innerText.trim();
      }),
    );
  }
  expect(order).toEqual([
    "Skip to content",
    "Exit Interview Agent",
    "Interview",
    "Account",
    "Sign out",
    "Polski",
    "radio:English", // the checked radio of the group is the stop; arrow keys move within it
    "select:tenure",
    "Continue",
    "Buy one interview",
    "Privacy",
  ]);
});

test("with reduced motion nothing animates or transitions @smoke", async ({ page, request }) => {
  await page.emulateMedia({ reducedMotion: "reduce" });
  const name = accountName("a11y-motion");
  await configure(request, name, "credits=1");
  await page.goto("/login?redirect=%2Finterview");
  await signIn(page, name);
  await page.getByRole("button", { name: "Continue" }).click();
  const moving = await page.evaluate(() =>
    [...document.querySelectorAll("*")]
      .map((el) => ({ el, s: getComputedStyle(el) }))
      .filter(({ s }) => parseFloat(s.animationDuration) > 0.01 || parseFloat(s.transitionDuration) > 0.01)
      .map(({ el }) => el.tagName.toLowerCase()),
  );
  expect(moving).toEqual([]);
});

for (const [label, viewport] of [
  ["390 px phone", { width: 390, height: 844 }],
  ["200% zoom of a 1280 px window", { width: 640, height: 400 }],
] as const) {
  test(`at ${label}, no page and no interview state scrolls sideways @smoke`, async ({ browser, request }) => {
    test.setTimeout(90_000);
    const context = await browser.newContext({ viewport, deviceScaleFactor: label.startsWith("200") ? 2 : 1, locale: "pl-PL" }); // Polish: the longer words
    const page = await context.newPage();
    for (const path of ["/", "/login", "/register", "/privacy", "/about", "/developers", "/connect", "/delete-submission"]) {
      await page.goto(path);
      await noSidewaysScroll(page, `${label} ${path}`);
    }
    await walk(page, request, "pl", (state) => noSidewaysScroll(page, `${label} ${state}`));
    await context.close();
  });
}
