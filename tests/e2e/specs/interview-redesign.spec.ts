import { expect, test, type APIRequestContext, type Page } from "@playwright/test";
import { STUB_URL, emailFor } from "../support/constants.mjs";
import { accountName, signIn } from "./support";

// The redesigned interview page (docs/ux/redesign-2026-10-11/phase2): the credit status line, the consent summary, the progress
// line, "Finish interview" (keeps the record) apart from "Stop and delete" (a modal dialog), "Send again" next to a kept answer,
// the countdown after a 429, and what the result says about its lifetime and the credit. Against the stub, as the other specs.

const configure = (request: APIRequestContext, name: string, query: string) =>
  request.post(`${STUB_URL}/__test/interview-config?email=${encodeURIComponent(emailFor(name))}&${query}`);

const stubState = async (request: APIRequestContext, name: string) =>
  (await (await request.get(`${STUB_URL}/__test/interview?email=${encodeURIComponent(emailFor(name))}`)).json()) as {
    credits: number;
    session: { id: string; status: string } | null;
  };

async function openInterview(page: Page, name: string) {
  await page.goto("/login?redirect=%2Finterview");
  await signIn(page, name);
  await expect(page.getByRole("heading", { name: "Exit interview with an AI" })).toBeVisible();
  await expect(page.getByText("Checking your credits…")).toHaveCount(0);
}

async function start(page: Page) {
  await page.getByRole("button", { name: "Continue" }).click();
  await page.getByRole("checkbox").check();
  await page.getByRole("button", { name: "Start the interview" }).click();
  await expect(page.getByRole("log", { name: "Conversation" })).toContainText("Hello. I am an AI interviewer.");
}

async function say(page: Page, text: string) {
  await expect(page.getByRole("status").filter({ hasText: "The interviewer is typing" })).toHaveCount(0);
  const answer = page.getByLabel("Your answer");
  await answer.fill(text);
  await answer.press("Enter");
}

test("the start screen says in one line whether an interview is ready, and Continue is the primary action @smoke", async ({ page, request }) => {
  const name = accountName("redesign");
  await configure(request, name, "credits=2");
  await openInterview(page, name);
  await expect(page.getByTestId("credit-status")).toContainText("2 interviews ready");
  await expect(page.getByRole("button", { name: "Continue" })).toBeEnabled();
  await expect(page.getByRole("button", { name: "Continue" })).toHaveClass(/primary/);
  await expect(page.getByRole("button", { name: "Buy one interview" })).toHaveClass(/secondary/);
});

test("a 402 at the start updates the balance next to the message: never '1 ready' beside 'no credit' @smoke", async ({ page, request }) => {
  const name = accountName("redesign");
  await configure(request, name, "credits=1");
  await openInterview(page, name);
  await expect(page.getByTestId("credit-status")).toContainText("1 interview ready");
  await page.getByRole("button", { name: "Continue" }).click();
  await configure(request, name, "credits=0"); // the credit went elsewhere (another tab) while this page showed 1
  await page.getByRole("checkbox").check();
  await page.getByRole("button", { name: "Start the interview" }).click();

  await expect(page.getByRole("alert").filter({ hasText: "You have no interview credit." })).toBeVisible();
  await expect(page.getByTestId("credit-status")).toContainText("No interview available");
  await expect(page.getByTestId("credit-status")).not.toContainText("ready");
});

test("the consent step puts the three facts that matter first, and the full terms open on request @smoke", async ({ page, request }) => {
  const name = accountName("redesign");
  await configure(request, name, "credits=1");
  await openInterview(page, name);
  await page.getByRole("button", { name: "Continue" }).click();

  const key = page.getByTestId("consent-key").getByRole("listitem");
  await expect(key).toHaveCount(3);
  await expect(key.nth(0)).toContainText("The interviewer is an AI");
  await expect(key.nth(1)).toContainText("Anthropic");
  await expect(key.nth(2)).toContainText("30 minutes");
  await expect(page.getByTestId("consent-terms-full")).toBeHidden();
  await page.getByText("Read the full terms").click();
  await expect(page.getByTestId("consent-terms-full")).toContainText("Nothing checks that you worked here.");
});

test("the chat shows its phase line, with no topic count, and Finish interview ends it early and keeps the record @smoke", async ({ page, request }) => {
  const name = accountName("redesign");
  await configure(request, name, "credits=1");
  await openInterview(page, name);
  await start(page);

  await expect(page.getByTestId("chat-progress")).toHaveText("Interview · usually 10–15 minutes · you can finish at any time");
  await expect(page.getByRole("button", { name: "Finish interview" })).toBeDisabled(); // not before the first topic question
  await say(page, "Onboarding was rough.");
  // The phase line does not count topics: after a question it reads the same, and there is no "of about" anywhere on the page.
  await expect(page.getByTestId("chat-progress")).toHaveText("Interview · usually 10–15 minutes · you can finish at any time");
  await expect(page.getByText(/of about \d/)).toHaveCount(0);

  // Send is the only primary button; the two ways out are separate, and say what they do.
  await expect(page.locator("button.primary")).toHaveText(["Send"]);
  const finish = page.getByRole("button", { name: "Finish interview" });
  await expect(finish).toHaveAccessibleDescription("Ends the conversation now. Your record and draft texts are kept.");
  await expect(page.getByRole("button", { name: "Stop and delete" })).toHaveAccessibleDescription(
    "Ends the conversation and deletes everything from it.",
  );

  await finish.click();
  // The request to finish is said in the conversation, in the interview's language, as the person's own answer.
  await expect(page.getByRole("log", { name: "Conversation" })).toContainText("Can we finish now?");
  await expect(page.getByRole("heading", { name: "Your record and draft texts" })).toBeVisible();
  expect((await stubState(request, name)).session?.status).toBe("completed");
});

test("Stop and delete opens a modal dialog: Cancel first and focused, focus kept inside, Escape cancels, focus comes back @smoke", async ({ page, request }) => {
  const name = accountName("redesign");
  await configure(request, name, "credits=1");
  await openInterview(page, name);
  await start(page);

  const opener = page.getByRole("button", { name: "Stop and delete" });
  await opener.click();
  const dialog = page.getByRole("dialog", { name: "Delete this interview?" });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByRole("button")).toHaveText(["Cancel", "Yes, delete it"]);
  await expect(dialog.getByRole("button", { name: "Cancel" })).toBeFocused();

  // Tab never leaves the dialog: the page behind it is inert.
  for (let i = 0; i < 4; i++) {
    await page.keyboard.press("Tab");
    expect(await page.evaluate(() => Boolean(document.activeElement?.closest("dialog")) || document.activeElement === document.body)).toBe(true);
  }

  await page.keyboard.press("Escape");
  await expect(dialog).toBeHidden();
  await expect(opener).toBeFocused();
  expect((await stubState(request, name)).session?.status).toBe("in_progress");

  await opener.click();
  await dialog.getByRole("button", { name: "Yes, delete it" }).click();
  await expect(page.getByTestId("deleted-notice")).toContainText("The interview was deleted. Nothing from it is kept.");
  await expect(page.getByTestId("deleted-notice")).toContainText("The credit it used is not returned");
  await expect(page.getByTestId("deleted-notice")).toContainText("To give another interview, buy one below.");
  // The chat that held the focus is gone: the confirmation takes it and is on screen, not scrolled out of sight.
  await expect(page.getByTestId("deleted-notice")).toBeFocused();
  await expect(page.getByTestId("deleted-notice")).toBeInViewport();
  expect(await stubState(request, name)).toEqual({ credits: 0, session: null });
});

test("when the interview ends, the result's heading takes the focus, and the chat never scrolled the page on its first render", async ({ page, request }) => {
  const name = accountName("redesign");
  await configure(request, name, "credits=1");
  await page.setViewportSize({ width: 390, height: 844 });
  await openInterview(page, name);
  await start(page);
  // On a phone the title bar and the opening question are where the person starts reading.
  await expect(page.getByTestId("chat-progress")).toBeInViewport();
  await expect(page.getByText("Hello. I am an AI interviewer.")).toBeInViewport();

  const box = page.getByLabel("Your answer");
  for (const text of ["Yes.", "Onboarding was chaotic.", "My manager was rarely there."]) {
    await box.fill(text);
    await box.press("Enter");
    await expect(page.getByRole("log", { name: "Conversation" })).toContainText(text);
  }
  const heading = page.getByRole("heading", { name: "Your record and draft texts" });
  await expect(heading).toBeFocused();
  await expect(heading).toBeInViewport();
});

test("a reply the model could not take is kept, and Send again sends it once the model is back", async ({ page, request }) => {
  const name = accountName("redesign");
  await configure(request, name, "credits=1");
  await openInterview(page, name);
  await start(page);

  await configure(request, name, "providerDown=1");
  await say(page, "My answer, kept.");
  await expect(page.getByRole("alert").filter({ hasText: "The AI model is not responding right now." })).toBeVisible();
  await expect(page.getByLabel("Your answer")).toHaveValue("My answer, kept.");
  const again = page.getByRole("button", { name: "Send again" });
  await expect(again).toBeEnabled();

  await configure(request, name, "providerDown=0");
  await again.click();
  await expect(page.getByLabel("Your answer")).toHaveValue("");
  await expect(page.getByRole("log", { name: "Conversation" })).toContainText("Thank you. How would you describe");
});

test("after a 429 on a reply, Send waits out the seconds the service asked for, then works again; nothing is sent by itself", async ({ page, request }) => {
  const name = accountName("redesign");
  await configure(request, name, "credits=1");
  await page.clock.install();
  await openInterview(page, name);
  await start(page);

  await configure(request, name, "replyRateLimited=1");
  await say(page, "Kept through the wait.");
  await expect(page.getByRole("alert").filter({ hasText: "Too many requests. Wait 30 seconds, then try again." })).toBeVisible();
  await expect(page.getByTestId("wait-countdown")).toHaveText("You can try again in 30 s.");
  const send = page.getByRole("button", { name: "Send again" });
  await expect(send).toBeDisabled();

  await configure(request, name, "replyRateLimited=0");
  await page.clock.runFor(10_000);
  await expect(page.getByTestId("wait-countdown")).toHaveText("You can try again in 20 s.");
  await expect(send).toBeDisabled();
  await page.clock.runFor(21_000);
  await expect(page.getByTestId("wait-countdown")).toHaveText("You can try again now.");
  await expect(send).toBeEnabled();
  expect((await stubState(request, name)).session?.status).toBe("in_progress");
  await expect(page.getByRole("log", { name: "Conversation" })).not.toContainText("Thank you. How would you describe"); // not sent by itself

  await send.click();
  await expect(page.getByRole("log", { name: "Conversation" })).toContainText("Thank you. How would you describe");
});

test("the result has one warning, one line for the topics not discussed, one Save control, and says until when it is readable @smoke", async ({ page, request }) => {
  const name = accountName("redesign");
  await configure(request, name, "credits=1");
  await openInterview(page, name);
  await start(page);
  await say(page, "Onboarding was chaotic.");
  await say(page, "My manager was rarely available.");
  await say(page, "One meeting was cancelled.");
  await expect(page.getByRole("heading", { name: "Your record and draft texts" })).toBeVisible();

  await expect(page.getByTestId("result-warning")).toHaveCount(1);
  await expect(page.getByTestId("result-warning")).toContainText("Drafts, not facts");
  await expect(page.getByText("These are drafts, not facts. Read and change every text")).toHaveCount(0); // the second box is gone
  await expect(page.getByTestId("not-discussed")).toHaveText(
    "Not discussed: Management, Growth, Pay compared with what was promised, Culture, Reason for leaving.",
  );
  await expect(page.getByText("Not discussed", { exact: true })).toHaveCount(0);
  await expect(page.getByTestId("result-summary")).toContainText(/readable here until about \d{2}:\d{2}, 30 minutes after the interview ended/);
  await expect(page.getByTestId("result-summary")).toContainText("This interview used one credit.");

  const save = page.getByTestId("save");
  await expect(save.locator("button")).toHaveCount(3); // the three downloads, behind one control
  await expect(save.getByRole("button", { name: "The record (JSON)" })).toBeHidden();
  await save.getByText("Save a copy").click();
  await expect(save.getByRole("button", { name: "The record (JSON)" })).toBeVisible();
});
