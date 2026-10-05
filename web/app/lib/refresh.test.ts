import { beforeEach, describe, expect, it, vi } from "vitest";
import { refreshSession, resetRefreshState } from "./refresh";
import type { AuthConfig } from "./runtime-config";

const cfg: AuthConfig = { url: "http://identity.invalid", issuer: "i", audience: "a", secureCookies: false };

const tokens = (n: number) => ({ accessToken: `access-${n}`, refreshToken: `refresh-${n}`, expiresIn: 600 });
const consentBody = { terms: { requiredVersion: "t1" }, privacy: { requiredVersion: "p1" }, requiresConsent: false };
const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });

/** An identity service that rotates: every refresh call consumes its token and answers with the next pair. */
function rotatingIdentity() {
  let issued = 0;
  const calls = { refresh: 0, consents: 0, reuse: 0 };
  const used = new Set<string>();
  const impl = vi.fn(async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input);
    if (url.endsWith("/auth/refresh")) {
      calls.refresh += 1;
      await new Promise((r) => setTimeout(r, 20)); // let concurrent callers pile up
      const presented = (JSON.parse(String(init?.body)) as { refreshToken: string }).refreshToken;
      if (used.has(presented)) {
        calls.reuse += 1; // authservice would revoke the whole family here
        return json(401, { error: "Invalid or expired refresh token" });
      }
      used.add(presented);
      issued += 1;
      return json(200, tokens(issued));
    }
    if (url.endsWith("/auth/consents")) {
      calls.consents += 1;
      return json(200, consentBody);
    }
    return json(404, {});
  });
  return { impl: impl as unknown as typeof fetch, calls };
}

describe("refreshSession (single-flight rotation)", () => {
  beforeEach(() => resetRefreshState());

  it("coalesces concurrent refreshes of one token into ONE upstream call, so rotation never sees a reuse", async () => {
    const identity = rotatingIdentity();

    const results = await Promise.all(Array.from({ length: 8 }, () => refreshSession(cfg, "refresh-0", { fetch: identity.impl })));

    expect(identity.calls).toEqual({ refresh: 1, consents: 1, reuse: 0 });
    for (const result of results) {
      expect(result).toMatchObject({ ok: true, session: { tokens: { accessToken: "access-1" } } });
    }
  });

  it("without coordination the same burst WOULD trip reuse detection (the test is capable of failing)", async () => {
    const identity = rotatingIdentity();
    const { callRefresh } = await import("./identity");

    await Promise.all(Array.from({ length: 3 }, () => callRefresh(cfg, "refresh-0", identity.impl)));

    expect(identity.calls.reuse).toBe(2);
  });

  it("serves requests that still carry the old cookie from the recent result, then rotates again once it is stale", async () => {
    const identity = rotatingIdentity();
    let now = 1_000_000;
    const deps = { fetch: identity.impl, now: () => now };

    await refreshSession(cfg, "refresh-0", deps);
    now += 5_000; // a parallel request that left the browser before Set-Cookie landed
    const late = await refreshSession(cfg, "refresh-0", deps);
    expect(identity.calls.refresh).toBe(1);
    expect(late).toMatchObject({ ok: true, session: { tokens: { accessToken: "access-1" } } });

    now += 60_000;
    await refreshSession(cfg, "refresh-0", deps); // stale entry gone: a real second call (here a replay, which the server refuses)
    expect(identity.calls.refresh).toBe(2);
    expect(identity.calls.reuse).toBe(1);
  });

  it("does not remember failures: a rejected token is asked about again", async () => {
    const impl = vi.fn(async () => json(401, { error: "Invalid or expired refresh token" })) as unknown as typeof fetch;

    const first = await refreshSession(cfg, "refresh-x", { fetch: impl });
    await new Promise((r) => setTimeout(r, 0));
    const second = await refreshSession(cfg, "refresh-x", { fetch: impl });

    expect(first).toEqual({ ok: false, reason: "invalid" });
    expect(second).toEqual({ ok: false, reason: "invalid" });
    expect(impl).toHaveBeenCalledTimes(2);
  });

  it("reports an unreachable identity service as unavailable, not as a dead session", async () => {
    const impl = vi.fn(async () => {
      throw new TypeError("fetch failed");
    }) as unknown as typeof fetch;

    expect(await refreshSession(cfg, "refresh-y", { fetch: impl })).toEqual({ ok: false, reason: "unavailable" });
  });

  it("keeps different refresh tokens independent", async () => {
    const identity = rotatingIdentity();

    await Promise.all([refreshSession(cfg, "refresh-a", { fetch: identity.impl }), refreshSession(cfg, "refresh-b", { fetch: identity.impl })]);

    expect(identity.calls.refresh).toBe(2);
  });
});
