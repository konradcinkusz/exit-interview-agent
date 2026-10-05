import { expect, test } from "@playwright/test";
import { accountName, signIn, watchViolations } from "./support";

// Hardening (T-06, ADR-0047, ADR-0048): a per-request nonce CSP with no inline-script allowance, no-store on everything
// that can show account data, a same-origin check on state-changing routes, nothing third-party.

const PUBLIC_PAGES = ["/", "/login", "/register", "/verify-email", "/connect", "/privacy", "/delete-submission", "/account-deleted"];
const SIGNED_IN_PAGES = ["/cli", "/account"];

const directive = (csp: string, name: string) => csp.split("; ").find((d) => d.startsWith(`${name} `)) ?? "";

test("every page carries a nonce CSP with no unsafe-inline for scripts or styles, and the nonce is new each time @smoke", async ({ request }) => {
  const nonces = new Set<string>();
  for (const path of [...PUBLIC_PAGES, "/login", "/login"]) {
    const response = await request.get(path);
    const csp = response.headers()["content-security-policy"] ?? "";
    const script = directive(csp, "script-src");

    const nonce = /'nonce-([^']+)'/.exec(script)?.[1];
    expect(nonce, path).toMatch(/^[A-Za-z0-9+/]{22}==$/);
    expect(script, path).not.toContain("unsafe-inline");
    expect(script, path).not.toContain("unsafe-eval");
    expect(directive(csp, "style-src"), path).not.toContain("unsafe-inline");
    expect(csp, path).toContain("frame-ancestors 'none'");
    expect(csp, path).toContain("connect-src 'self'");
    nonces.add(nonce!);
  }
  expect(nonces.size).toBe(PUBLIC_PAGES.length + 2); // one fresh nonce per response
});

test("the nonce in the header is the one stamped on the page's own scripts, and no script is left unnonced @smoke", async ({ request }) => {
  const response = await request.get("/login");
  const nonce = /'nonce-([^']+)'/.exec(response.headers()["content-security-policy"] ?? "")?.[1];
  const html = await response.text();

  const inlineScripts = [...html.matchAll(/<script(?![^>]*\ssrc=)([^>]*)>/g)];
  expect(inlineScripts.length).toBeGreaterThan(0);
  for (const [, attrs] of inlineScripts) expect(attrs).toContain(`nonce="${nonce}"`);
  expect(html).not.toMatch(/\son[a-z]+=/i); // no inline event handlers
  expect(html).not.toMatch(/\sstyle="/i); // no style attributes, which style-src would refuse
});

test("no page raises a CSP violation, loads anything from another origin, or fails to hydrate @smoke", async ({ page }) => {
  const seen = await watchViolations(page);
  const errors: string[] = [];
  page.on("pageerror", (e) => errors.push(e.message));

  for (const path of PUBLIC_PAGES) {
    await page.goto(path);
    await page.waitForLoadState("networkidle");
  }
  await page.goto("/login?redirect=%2Faccount");
  await signIn(page, accountName("csp"));
  for (const path of SIGNED_IN_PAGES) {
    await page.goto(path);
    await page.waitForLoadState("networkidle");
  }

  expect(seen.csp).toEqual([]);
  expect(seen.foreign).toEqual([]);
  expect(errors).toEqual([]);
});

test("the CSP really blocks an injected inline script (the policy is enforced, not just present) @smoke", async ({ page }) => {
  const seen = await watchViolations(page);
  await page.goto("/privacy");

  // Two injection shapes: a script element with inline code, and an inline event handler in markup.
  const ran = await page.evaluate(async () => {
    const script = document.createElement("script");
    script.textContent = "window.__injected = 'script'";
    document.body.appendChild(script);
    const holder = document.createElement("div");
    holder.innerHTML = '<img src="data:," alt="" onerror="window.__injected = \'handler\'">';
    document.body.appendChild(holder);
    await new Promise((resolve) => setTimeout(resolve, 200));
    return (window as unknown as { __injected?: string }).__injected ?? null;
  });

  expect(ran).toBeNull();
  await expect.poll(() => seen.csp.length).toBeGreaterThan(0);
});

test("pages and API answers that can show account data, a ticket or a receipt outcome are never stored @smoke", async ({ page, request }) => {
  for (const path of ["/", "/login", "/connect", "/privacy", "/delete-submission", "/account-deleted", "/api/auth/session"]) {
    expect((await request.get(path)).headers()["cache-control"], path).toBe("no-store");
  }

  await page.goto("/login?redirect=%2Faccount");
  await signIn(page, accountName("nostore"));
  for (const path of ["/account", "/cli", "/api/proxy/v1/me", "/api/auth/export", "/api/auth/consent"]) {
    const response = await page.request.get(path);
    expect(response.headers()["cache-control"], path).toBe("no-store");
  }

  // The two cacheable probes keep their own short lifetime; they hold nothing per user.
  expect((await request.get("/api/config")).headers()["cache-control"]).toContain("max-age=10");
});

test("a state-changing request from another origin is refused before any handler runs @smoke", async ({ request }) => {
  for (const [method, path] of [["POST", "/api/auth/login"], ["DELETE", "/api/receipts"], ["DELETE", "/api/auth/account"], ["POST", "/api/auth/session"]] as const) {
    const evil = await request.fetch(path, { method, headers: { Origin: "https://evil.example.invalid", "content-type": "application/json" }, data: "{}" });
    expect(evil.status(), `${method} ${path} with a foreign Origin`).toBe(403);
    expect(await evil.json()).toEqual({ error: "cross_origin_request" });

    const crossSite = await request.fetch(path, { method, headers: { "Sec-Fetch-Site": "cross-site", "content-type": "application/json" }, data: "{}" });
    expect(crossSite.status(), `${method} ${path} marked cross-site`).toBe(403);
  }
});

test("a same-origin request is not refused by the check, and a safe method never is @smoke", async ({ request }) => {
  const same = await request.post("/api/auth/login", { headers: { Origin: "http://localhost:4011" }, data: { email: "x", password: "y" } });
  expect(same.status()).toBe(401); // got past the check; the identity service refused the fake credentials
  expect(await same.json()).toEqual({ error: "invalid_credentials" });

  expect((await request.get("/api/config", { headers: { Origin: "https://evil.example.invalid" } })).status()).toBe(200);
});

test("the session cookies are SameSite=Strict and HttpOnly, and none holds anything a page can read @smoke", async ({ page, context }) => {
  await page.goto("/login");
  await signIn(page, accountName("cookies"));
  await expect(page).toHaveURL(/\/account$/);

  const cookies = (await context.cookies()).filter((c) => c.name.startsWith("eia_"));
  expect(cookies.length).toBeGreaterThan(0);
  for (const cookie of cookies) {
    expect(cookie.httpOnly, cookie.name).toBe(true);
    expect(cookie.sameSite, cookie.name).toBe("Strict");
  }
  expect(await page.evaluate(() => document.cookie)).toBe("");
});

test("an unknown page is a 404 with a way home, and the error text names no internals @smoke", async ({ page }) => {
  // The edge gate fails closed: a signed-out visitor is sent to sign in for ANY path that is not on the public list, so an
  // unknown path never discloses which paths exist. A signed-in visitor reaches the 404 page.
  await page.goto("/login?redirect=%2Faccount");
  await signIn(page, accountName("notfound"));
  await expect(page).toHaveURL(/\/account$/);
  const response = await page.goto("/does-not-exist");

  expect(response?.status()).toBe(404);
  await expect(page.getByTestId("not-found")).toHaveText("There is nothing at this address.");
  await expect(page.getByRole("link", { name: "Back to the start" })).toBeVisible();
});

test("the security header set is on pages and API answers alike @smoke", async ({ request }) => {
  for (const path of ["/login", "/connect", "/api/config", "/api/receipts"]) {
    const headers = (await request.get(path)).headers();
    expect(headers["x-frame-options"], path).toBe("DENY");
    expect(headers["x-content-type-options"], path).toBe("nosniff");
    expect(headers["referrer-policy"], path).toBe("no-referrer");
    expect(headers["permissions-policy"], path).toContain("camera=()");
    expect(headers["cross-origin-opener-policy"], path).toBe("same-origin");
    expect(headers["x-powered-by"], path).toBeUndefined();
  }
});
