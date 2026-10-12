import AxeBuilder from "@axe-core/playwright";
import { expect, test, type APIRequestContext, type Page, type Route } from "@playwright/test";
import { STUB_URL, emailFor } from "../support/constants.mjs";
import { accountName, signIn, watchViolations } from "./support";

// The platform drafts on the result page (platform-drafts-spec §3.5, WP5), in both languages. The stub's own draft is one Glassdoor draft,
// so each test asks the stub for a real result and replaces `tiles.drafts` with its synthetic drafts on the way to the page. Everything
// else the page shows still comes from the stub.

const TAGS = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"];

const configure = (request: APIRequestContext, name: string, query: string) =>
  request.post(`${STUB_URL}/__test/interview-config?email=${encodeURIComponent(emailFor(name))}&${query}`);

const DRAFTS_EN = [
  {
    platform: "glassdoor",
    name: "Glassdoor",
    family: "structured_review",
    risk: "medium",
    url: "https://www.glassdoor.com/",
    warning: "Draft, not fact. Not legal advice.",
    sections: [
      { key: "title", label: "Headline", text: "Good onboarding, unclear growth" },
      { key: "pros", label: "Pros", text: "The onboarding plan was clear and the team helped in week one." },
    ],
    copyText: "Good onboarding, unclear growth\n\nThe onboarding plan was clear and the team helped in week one.",
    fieldsOnSite: ["Employment status and job title", "Star ratings"],
  },
  {
    platform: "linkedin",
    name: "LinkedIn",
    family: "professional_post",
    risk: "high",
    url: "https://www.linkedin.com/",
    warning: "Draft, not fact. Not legal advice. Your name is on this post.",
    sections: [{ key: "body", label: "", text: "My last year there taught me what I want from a manager." }],
    copyText: "My last year there taught me what I want from a manager.",
    fieldsOnSite: ["The post text, written by you"],
  },
];

const DRAFTS_PL = [
  {
    platform: "gowork",
    name: "GoWork.pl",
    family: "structured_review",
    risk: "high",
    url: "https://www.gowork.pl/",
    warning: "To szkic, nie fakt. To nie jest porada prawna.",
    sections: [{ key: "pros", label: "Plusy", text: "Dobry zespół i jasne zasady pracy." }],
    copyText: "Dobry zespół i jasne zasady pracy.",
    fieldsOnSite: ["Status zatrudnienia", "Stanowisko"],
  },
];

/** Answers the result request with the stub's real result, with `drafts` added to its tiles. */
async function withDrafts(page: Page, drafts: unknown[]) {
  await page.route("**/api/proxy/v1/interviews/*/result", async (route: Route) => {
    const response = await route.fetch();
    const body = (await response.json()) as { tiles: Record<string, unknown> };
    body.tiles.drafts = drafts;
    await route.fulfill({ response, json: body });
  });
}

async function openInterview(page: Page, name: string, language: "en" | "pl") {
  await page.goto("/login?redirect=%2Finterview");
  await signIn(page, name);
  await expect(page.getByRole("heading", { name: language === "en" ? "Exit interview with an AI" : "Rozmowa wyjściowa z AI" })).toBeVisible();
  await expect(page.getByText("Checking your credits…")).toHaveCount(0);
}

/** Runs the chat to its finish with the stub, and waits for the result. `finish` and the answers are the words of the given language. */
async function finishInterview(page: Page, language: "en" | "pl") {
  const en = language === "en";
  await page.getByRole("button", { name: en ? "Continue" : "Dalej" }).click();
  await page.getByRole("checkbox").check();
  await page.getByRole("button", { name: en ? "Start the interview" : "Rozpocznij rozmowę" }).click();
  const answer = page.getByLabel(en ? "Your answer" : "Twoja odpowiedź");
  const send = page.getByRole("button", { name: en ? "Send" : "Wyślij" });
  for (const text of en ? ["It was chaotic.", "My manager was rarely available."] : ["Było chaotycznie.", "Przełożony był rzadko dostępny."]) {
    await answer.fill(text);
    await send.click();
  }
  await page.getByRole("button", { name: en ? "Finish interview" : "Zakończ rozmowę" }).click();
  await expect(page.getByRole("heading", { name: en ? "Your record and draft texts" : "Zapis i szkice tekstów" })).toBeVisible();
}

test("English drafts: each platform's risk in words, one copy per section, the high-risk gate, and a link that opens a new tab", async ({ page, context, request }) => {
  await context.grantPermissions(["clipboard-read", "clipboard-write"]);
  const name = accountName("drafts-en");
  await configure(request, name, "credits=1");
  const seen = await watchViolations(page);
  await withDrafts(page, DRAFTS_EN);

  await openInterview(page, name, "en");
  await page.getByRole("radio", { name: "English" }).check();
  await page.getByLabel("How long did you work there?").selectOption("1y_3y");
  await finishInterview(page, "en");

  const drafts = page.getByRole("heading", { name: "Drafts for a platform" });
  await expect(drafts).toBeVisible();

  // Glassdoor: medium risk, so its copy works at once. The copy puts exactly the section's text on the clipboard.
  const glassdoor = page.getByRole("article", { name: "Glassdoor" });
  await expect(glassdoor.getByText("Medium risk")).toBeVisible();
  await expect(glassdoor.getByRole("note")).toHaveText("Draft, not fact. Not legal advice.");
  await expect(glassdoor.getByRole("button", { name: "Copy: Glassdoor, Pros" })).toBeEnabled();
  await glassdoor.getByRole("button", { name: "Copy: Glassdoor, Pros" }).click();
  await expect(glassdoor.getByRole("button", { name: "Copy: Glassdoor, Pros" }).locator("xpath=..").getByRole("status")).toHaveText("Copied");
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe("The onboarding plan was clear and the team helped in week one.");
  await expect(glassdoor.getByRole("link", { name: /Open Glassdoor/ })).toHaveAttribute("href", "https://www.glassdoor.com/");
  await expect(glassdoor.getByRole("link", { name: /Open Glassdoor/ })).toHaveAttribute("target", "_blank");
  await expect(glassdoor.getByRole("link", { name: /Open Glassdoor/ })).toHaveAttribute("rel", "noopener noreferrer");
  await expect(glassdoor.getByText("(opens a new tab)")).toBeVisible();

  // LinkedIn: high risk. Its copy buttons are disabled until the warning is acknowledged, and the box is reached by the keyboard.
  const linkedin = page.getByRole("article", { name: "LinkedIn" });
  await expect(linkedin.getByText("Higher risk")).toBeVisible();
  await expect(linkedin.getByRole("button", { name: "Copy: LinkedIn" })).toBeDisabled();
  await expect(linkedin.getByRole("button", { name: "Copy all" })).toBeDisabled();
  const box = linkedin.getByRole("checkbox", { name: "I have read this warning" });
  await box.focus();
  await expect(box).toBeFocused();
  await page.keyboard.press("Space");
  await expect(box).toBeChecked();
  await expect(linkedin.getByRole("button", { name: "Copy: LinkedIn" })).toBeEnabled();
  await linkedin.getByRole("button", { name: "Copy: LinkedIn" }).click();
  await expect(linkedin.getByRole("status").filter({ hasText: "Copied" })).toHaveCount(1);
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe("My last year there taught me what I want from a manager.");

  // No network leaves the page for a copy, and nothing foreign loads.
  expect(seen.csp).toEqual([]);
  expect(seen.foreign).toEqual([]);

  // The drafts section passes the same WCAG 2.0/2.1 A and AA rules as the rest of the interview.
  const results = await new AxeBuilder({ page }).withTags(TAGS).analyze();
  expect(results.violations.map((v) => v.id)).toEqual([]);
});

test.describe("Polish portal", () => {
  test.use({ locale: "pl-PL" });

  test("Polish drafts: the Polish heading, the warning as a Polish note, and the Polish gate words", async ({ page, context, request }) => {
    await context.grantPermissions(["clipboard-read", "clipboard-write"]);
    const name = accountName("drafts-pl");
    await configure(request, name, "credits=1");
    await withDrafts(page, DRAFTS_PL);

    await openInterview(page, name, "pl");
    await finishInterview(page, "pl");

    await expect(page.getByRole("heading", { name: "Szkice dla serwisów" })).toBeVisible();
    const gowork = page.getByRole("article", { name: "GoWork.pl" });
    await expect(gowork.getByText("Wyższe ryzyko")).toBeVisible();
    const warning = gowork.getByRole("note");
    await expect(warning).toHaveText("To szkic, nie fakt. To nie jest porada prawna.");
    await expect(warning).toHaveAttribute("lang", "pl");
    await expect(gowork.getByRole("button", { name: "Kopiuj: GoWork.pl, Plusy" })).toBeDisabled();
    await gowork.getByRole("checkbox", { name: "Przeczytałem/am to ostrzeżenie" }).check();
    await expect(gowork.getByRole("button", { name: "Kopiuj: GoWork.pl, Plusy" })).toBeEnabled();
    await expect(gowork.getByRole("link", { name: /Otwórz GoWork.pl/ })).toHaveAttribute("href", "https://www.gowork.pl/");
    await expect(gowork.getByText("Uzupełnisz w serwisie:")).toBeVisible();
  });
});
