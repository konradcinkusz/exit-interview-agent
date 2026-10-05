import type { APIRequestContext, Page } from "@playwright/test";
import { FAKE_PASSWORD, STUB_URL, emailFor } from "../support/constants.mjs";

export interface StubStats {
  consentAccepted: boolean;
  lastLocale: string | null;
  refreshCalls: number;
  reuseDetected: number;
  logouts: number;
  revoked: boolean;
  deleted: boolean;
}

/** Every test owns an account: the stub creates it on first login, so tests stay independent and parallel-safe. */
export const accountName = (prefix: string) => `${prefix}-${Math.random().toString(36).slice(2, 8)}`;

export async function signIn(page: Page, name: string, password = FAKE_PASSWORD) {
  await page.getByTestId("login-email").fill(emailFor(name));
  await page.getByTestId("login-password").fill(password);
  await page.getByTestId("login-submit").click();
}

export async function stats(request: APIRequestContext, name: string): Promise<StubStats> {
  const response = await request.get(`${STUB_URL}/__test/stats?email=${encodeURIComponent(emailFor(name))}`);
  return (await response.json()) as StubStats;
}

export interface Violations {
  /** `securitypolicyviolation` events and console messages from the browser's CSP enforcement. */
  csp: string[];
  /** Requests to any origin other than the app's own (the page must load nothing third-party). */
  foreign: string[];
}

/** Starts collecting CSP violations and foreign requests for a page. Call before the first navigation. */
export async function watchViolations(page: Page, origin = "http://localhost:4011"): Promise<Violations> {
  const seen: Violations = { csp: [], foreign: [] };
  await page.addInitScript(() => {
    document.addEventListener("securitypolicyviolation", (e) => {
      console.error(`CSPVIOLATION ${e.violatedDirective} ${e.blockedURI}`);
    });
  });
  page.on("console", (message) => {
    const text = message.text();
    if (text.startsWith("CSPVIOLATION") || /Content Security Policy|Refused to (execute|apply|load)/i.test(text)) seen.csp.push(text);
  });
  page.on("request", (request) => {
    const url = new URL(request.url());
    if (url.protocol.startsWith("http") && url.origin !== origin) seen.foreign.push(request.url());
  });
  return seen;
}

/** Everything a page could use to remember a secret: both storages, document.cookie, IndexedDB names and the URL. */
export async function browserMemory(page: Page) {
  return page.evaluate(async () => ({
    local: JSON.stringify({ ...localStorage }),
    session: JSON.stringify({ ...sessionStorage }),
    cookie: document.cookie,
    url: location.href,
    databases: JSON.stringify(await (indexedDB.databases?.() ?? Promise.resolve([]))),
  }));
}
