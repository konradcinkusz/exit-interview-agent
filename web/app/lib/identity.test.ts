import { describe, expect, it, vi } from "vitest";
import { acceptCurrentConsents, callRefresh, deleteAccount, fetchConsentStatus, localeFrom, parseConsentStatus, revokeSessions } from "./identity";
import type { AuthConfig } from "./runtime-config";

const cfg: AuthConfig = { url: "http://identity.invalid", issuer: "i", audience: "a", secureCookies: false };
const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status });
const fetchOf = (response: Response | (() => Response) | Error) =>
  vi.fn(async () => {
    if (response instanceof Error) throw response;
    return typeof response === "function" ? response() : response.clone();
  }) as unknown as typeof fetch;

// authservice's ConsentStatusResponse, as its JSON serializer writes it.
const status = (requiresConsent: boolean) => ({
  terms: { requiredVersion: "2026-02-01", acceptedVersion: "2026-01-01", accepted: !requiresConsent },
  privacy: { requiredVersion: "2026-01-01", acceptedVersion: "2026-01-01", accepted: true },
  cookies: { requiredVersion: "2026-01-01", accepted: false },
  requiresConsent,
});

describe("consent status", () => {
  it("reads the versions in force and whether acceptance is required", () => {
    expect(parseConsentStatus(status(true))).toEqual({ required: true, terms: "2026-02-01", privacy: "2026-01-01" });
    expect(parseConsentStatus(status(false))).toMatchObject({ required: false });
  });

  it("treats anything unexpected as unknown, never as accepted", () => {
    expect(parseConsentStatus(null)).toBeNull();
    expect(parseConsentStatus({})).toBeNull();
    expect(parseConsentStatus({ requiresConsent: "no", terms: {}, privacy: {} })).toBeNull();
  });

  it("is null when authservice errors or cannot be reached", async () => {
    expect(await fetchConsentStatus(cfg, "t", fetchOf(json(500, {})))).toBeNull();
    expect(await fetchConsentStatus(cfg, "t", fetchOf(new TypeError("down")))).toBeNull();
  });

  it("accepts with a plain yes for both documents and lets authservice pick the versions", async () => {
    const impl = fetchOf(json(200, status(false)));

    const result = await acceptCurrentConsents(cfg, "tok", "en-GB", impl);

    expect(result?.required).toBe(false);
    const [, init] = (impl as unknown as { mock: { calls: [string, RequestInit][] } }).mock.calls[0];
    expect(JSON.parse(String(init.body))).toEqual({ acceptedTerms: true, acceptedPrivacy: true, locale: "en-GB" });
    expect(String(init.body)).not.toMatch(/2026/); // no version is sent by the client
  });
});

describe("localeFrom", () => {
  it.each([
    ["en-GB,en;q=0.9", "en-GB"],
    ["pl", "pl"],
    ["", undefined],
    ["<script>", undefined],
    [null, undefined],
  ])("%s -> %s", (header, expected) => expect(localeFrom(header)).toBe(expected));
});

describe("callRefresh", () => {
  it("returns the rotated pair", async () => {
    const out = await callRefresh(cfg, "r", fetchOf(json(200, { accessToken: "a", refreshToken: "r2", expiresIn: 300 })));

    expect(out).toEqual({ ok: true, tokens: { accessToken: "a", refreshToken: "r2", expiresIn: 300 } });
  });

  it.each([[401, "invalid"], [400, "invalid"], [500, "unavailable"], [429, "unavailable"]])("HTTP %i -> %s", async (code, reason) => {
    expect(await callRefresh(cfg, "r", fetchOf(json(code, {})))).toEqual({ ok: false, reason });
  });

  it("is unavailable when the answer has no usable tokens", async () => {
    expect(await callRefresh(cfg, "r", fetchOf(json(200, { accessToken: "a" })))).toEqual({ ok: false, reason: "unavailable" });
  });
});

describe("deleteAccount", () => {
  it("passes the password and the confirmation through and reports success", async () => {
    const impl = fetchOf(json(200, { message: "Account deleted successfully" }));

    expect(await deleteAccount(cfg, "tok", { password: "pw", confirmation: "DELETE" }, impl)).toEqual({ ok: true });
    const [url, init] = (impl as unknown as { mock: { calls: [string, RequestInit][] } }).mock.calls[0];
    expect(url).toBe("http://identity.invalid/api/v1/auth/account");
    expect(init.method).toBe("DELETE");
    expect(JSON.parse(String(init.body))).toEqual({ password: "pw", confirmation: "DELETE" });
  });

  it.each([
    [400, { error: "Password is required" }, "password_required"],
    [400, { error: "Invalid password" }, "invalid_password"],
    [400, { errors: ["x"] }, "confirmation_required"],
    [401, {}, "unauthenticated"],
    [500, {}, "identity_unavailable"],
  ])("HTTP %i %j -> %s", async (code, body, error) => {
    expect(await deleteAccount(cfg, "tok", { confirmation: "DELETE" }, fetchOf(json(code, body)))).toEqual({ ok: false, error });
  });

  it("is unavailable, and deletes nothing, when authservice cannot be reached", async () => {
    expect(await deleteAccount(cfg, "tok", { confirmation: "DELETE" }, fetchOf(new TypeError("down")))).toEqual({ ok: false, error: "identity_unavailable" });
  });
});

describe("revokeSessions", () => {
  it("never throws", async () => {
    expect(await revokeSessions(cfg, "t", fetchOf(new TypeError("down")))).toBe(false);
    expect(await revokeSessions(cfg, "t", fetchOf(json(200, {})))).toBe(true);
  });
});
