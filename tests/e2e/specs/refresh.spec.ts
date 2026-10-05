import { expect, test } from "@playwright/test";
import { accountName, signIn, stats } from "./support";

// Refresh rotation (IDENTITY-AND-ACCOUNTS §2): the stub rotates single-use refresh tokens like authservice does and
// revokes the account when a consumed one is presented again. A lapsed access cookie is simulated by deleting it.

test("a lapsed access token is rotated transparently and the visitor stays on the page @smoke", async ({ page, context, request }) => {
  const name = accountName("refresh");
  await page.goto("/login");
  await signIn(page, name);
  await expect(page.getByTestId("account-subject")).toBeVisible();
  const before = (await context.cookies()).find((c) => c.name === "eia_refresh")?.value;

  await context.clearCookies({ name: "eia_access" });
  await page.goto("/account");

  await expect(page).toHaveURL(/\/account$/);
  await expect(page.getByTestId("account-subject")).toHaveText(`account-${name}`);
  const cookies = await context.cookies();
  const after = cookies.find((c) => c.name === "eia_refresh");
  expect(after?.httpOnly).toBe(true);
  expect(after?.value).not.toBe(before); // rotated: the old token is spent
  expect(cookies.find((c) => c.name === "eia_access")?.httpOnly).toBe(true);
  expect((await stats(request, name)).refreshCalls).toBe(1);
});

test("parallel requests after the access token lapsed cause exactly one rotation and no reuse @smoke", async ({ page, context, request }) => {
  const name = accountName("refresh");
  await page.goto("/login");
  await signIn(page, name);
  await expect(page.getByTestId("account-subject")).toBeVisible();
  await context.clearCookies({ name: "eia_access" });

  const responses = await Promise.all(Array.from({ length: 6 }, () => page.request.get("/api/proxy/v1/me")));

  expect(responses.map((r) => r.status())).toEqual([200, 200, 200, 200, 200, 200]);
  const seen = await stats(request, name);
  expect(seen.refreshCalls).toBe(1);
  expect(seen.reuseDetected).toBe(0);
  expect(seen.revoked).toBe(false);
});

test("a revoked session cannot be rotated and sends the visitor to sign in @smoke", async ({ page, context, request }) => {
  const name = accountName("refresh");
  await page.goto("/login");
  await signIn(page, name);
  await expect(page.getByTestId("account-subject")).toBeVisible();
  await request.post(`http://localhost:4010/__test/revoke?email=${encodeURIComponent(`${name}@example.invalid`)}`);
  await context.clearCookies({ name: "eia_access" });

  await page.goto("/account");

  await expect(page).toHaveURL(/\/login\?redirect=%2Faccount$/);
  expect((await context.cookies()).find((c) => c.name === "eia_refresh")).toBeUndefined();
});

test("signing out revokes the session at the identity service, not just the cookies @smoke", async ({ page, request }) => {
  const name = accountName("logout");
  await page.goto("/login");
  await signIn(page, name);
  await expect(page.getByTestId("account-subject")).toBeVisible();

  await page.getByTestId("logout").click();

  await expect(page).toHaveURL("/");
  const seen = await stats(request, name);
  expect(seen.logouts).toBe(1);
  expect(seen.revoked).toBe(true);
});
