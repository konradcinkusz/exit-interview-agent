import { expect, test } from "@playwright/test";
import { accountName, signIn } from "./support";

// The product pages say what the system can and cannot do (docs/ux/UI-UX.md principle 1). The words that state a fact are
// asserted here so the page and the design cannot drift apart.

test("the landing page says what is stored, what is not, and the non-goals @smoke", async ({ page }) => {
  await page.goto("/");

  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Exit Interview Agent");
  await expect(page.getByText("The interview transcript. It stays in your AI client or on your computer.")).toBeVisible();
  await expect(page.getByText("A link between your account and a submission.")).toBeVisible();
  await expect(page.getByText("Not a public review site")).toBeVisible();
  await expect(page.getByText("never calls records anonymous")).toBeVisible();
  await expect(page.locator("main")).not.toContainText(/\banonymi[sz]ed\b/i);
});

test("the connect page shows the connector address from the runtime config and what the connector cannot do @smoke", async ({ page }) => {
  await page.goto("/connect");

  await expect(page.getByTestId("mcp-url")).toHaveText("https://mcp.example.invalid/mcp");
  await expect(page.getByTestId("connect-cannot")).toContainText("See your conversation");
  await expect(page.getByTestId("connect-cannot")).toContainText("Read, list or change records");
  await expect(page.getByText("Claude is the AI client that is supported today")).toBeVisible();
  await expect(page.locator("main")).not.toContainText(/todo|coming soon|tbd/i);
});

test("the connect page points to the operator runbook in the project repository and shows its path @smoke", async ({ page }) => {
  await page.goto("/connect");

  const runbook = page.getByTestId("connect-runbook");
  await expect(runbook.getByRole("link", { name: "Connect Claude: operator runbook" })).toHaveAttribute(
    "href",
    "https://github.com/konradcinkusz/exit-interview-agent/blob/main/docs/guides/connect-claude.md",
  );
  await expect(runbook).toContainText("docs/guides/connect-claude.md");
});

test("the privacy page lists the key facts and the limits, and links to the full documents @smoke", async ({ page }) => {
  await page.goto("/privacy");

  await expect(page.getByTestId("privacy-facts")).toContainText("it is still personal data");
  await expect(page.getByTestId("privacy-facts")).toContainText("Your account is not linked to your submissions");
  await expect(page.getByText("An operator who has the database, the secret key")).toBeVisible();
  await expect(page.getByRole("link", { name: "Privacy design" })).toHaveAttribute("href", /DESIGN\.md$/);
});

test("the account page offers the data export and says submissions are not in it @smoke", async ({ page }) => {
  await page.goto("/login?redirect=%2Faccount");
  await signIn(page, accountName("export"));

  await expect(page.getByTestId("export-note")).toContainText("Your submissions are not part of it");
  await expect(page.getByTestId("export-note")).toContainText("not linked to your account");

  const [download] = await Promise.all([page.waitForEvent("download"), page.getByTestId("export-link").click()]);
  expect(download.suggestedFilename()).toBe("authservice-export.json");
});

test("the export is for the signed-in account only @smoke", async ({ request }) => {
  const response = await request.get("/api/auth/export", { maxRedirects: 0 });
  expect(response.status()).toBe(401);
});

test("the proxy refuses everything that is not a versioned API path and never forwards a receipt header @smoke", async ({ page }) => {
  await page.goto("/login?redirect=%2Faccount");
  await signIn(page, accountName("proxy"));
  await expect(page).toHaveURL(/\/account$/);

  expect((await page.request.get("/api/proxy/health")).status()).toBe(404);
  expect((await page.request.get("/api/proxy/v1/../health")).status()).toBe(404);
});
