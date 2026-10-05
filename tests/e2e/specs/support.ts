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
