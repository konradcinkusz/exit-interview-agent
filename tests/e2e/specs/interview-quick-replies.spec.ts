import { expect, test, type APIRequestContext, type Page } from "@playwright/test";
import { STUB_URL, emailFor } from "../support/constants.mjs";
import { accountName, signIn } from "./support";

// The consent turn of the chat offers quick answers (spec §5.3, WP7). Each button sends exactly its text as the person's answer,
// with a keyboard as well as a mouse; what was typed stays in the box; the group goes once the interviewer has moved on from the
// consent question. Against the stub, in English and in Polish.

const configure = (request: APIRequestContext, name: string, query: string) =>
  request.post(`${STUB_URL}/__test/interview-config?email=${encodeURIComponent(emailFor(name))}&${query}`);

/** Signs in, opens the interview and starts it, so the stub's opening (the consent turn) is the last line of the chat. */
async function startChat(page: Page, request: APIRequestContext, copy: { title: string; cont: string; start: string; greeting: string }) {
  const name = accountName("quick");
  await configure(request, name, "credits=1");
  await page.goto("/login?redirect=%2Finterview");
  await signIn(page, name);
  await expect(page.getByRole("heading", { name: copy.title })).toBeVisible();
  await expect(page.getByRole("button", { name: copy.cont })).toBeEnabled();
  await page.getByRole("button", { name: copy.cont }).click();
  await page.getByRole("checkbox").check();
  await page.getByRole("button", { name: copy.start }).click();
  await expect(page.getByRole("log")).toContainText(copy.greeting);
}

test.describe("in English", () => {
  test.use({ locale: "en-US" });

  test("the consent turn offers Yes and No; a keyboard press sends the text and keeps what is typed", async ({ page, request }) => {
    await startChat(page, request, {
      title: "Exit interview with an AI",
      cont: "Continue",
      start: "Start the interview",
      greeting: "Hello. I am an AI interviewer.",
    });

    const group = page.getByRole("group", { name: "Quick answers" });
    await expect(group.getByRole("button")).toHaveText(["Yes", "No"]);
    // Quick answers are secondary: Send stays the only primary button.
    await expect(page.locator("button.primary")).toHaveText(["Send"]);

    await page.getByLabel("Your answer").fill("Still typing this");
    const yes = group.getByRole("button", { name: "Yes", exact: true });
    await yes.focus();
    await page.keyboard.press("Enter");

    const log = page.getByRole("log", { name: "Conversation" });
    await expect(log).toContainText("You: Yes");
    await expect(page.getByLabel("Your answer")).toHaveValue("Still typing this");
    // The interviewer has moved on to a topic question: the consent answers are gone, and focus is in the answer box.
    await expect(group).toHaveCount(0);
    await expect(page.getByLabel("Your answer")).toBeFocused();
    await expect(page.getByText(/of about \d/)).toHaveCount(0);
  });
});

test.describe("in Polish", () => {
  test.use({ locale: "pl-PL" });

  test("the consent turn offers the five Polish answers; a keyboard press sends the text exactly", async ({ page, request }) => {
    await startChat(page, request, {
      title: "Rozmowa wyjściowa z AI",
      cont: "Dalej",
      start: "Rozpocznij rozmowę",
      greeting: "Dzień dobry. Jestem rozmówcą AI.",
    });

    const group = page.getByRole("group", { name: "Szybkie odpowiedzi" });
    await expect(group.getByRole("button")).toHaveText(["Tak", "Tak, per Pani", "Tak, per Pan", "Tak, na Ty", "Nie"]);

    await group.getByRole("button", { name: "Tak, per Pani", exact: true }).focus();
    await page.keyboard.press("Enter");

    await expect(page.getByRole("log", { name: "Rozmowa" })).toContainText("Ty: Tak, per Pani");
    await expect(group).toHaveCount(0);
    await expect(page.getByLabel("Twoja odpowiedź")).toBeFocused();
  });
});
