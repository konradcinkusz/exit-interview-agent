import { describe, expect, it, vi } from "vitest";
import {
  acceptCurrentConsents,
  callRefresh,
  deleteAccount,
  fetchConsentStatus,
  fetchConsentVersions,
  fetchExport,
  localeFrom,
  loginWithTwoFactor,
  parseConsentStatus,
  registerAccount,
  resendVerification,
  revokeSessions,
  verifyEmail,
} from "./identity";
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

describe("two-factor sign-in (authservice 2fa/login)", () => {
  it("posts the challenge with a code and returns the tokens", async () => {
    const impl = fetchOf(json(200, { accessToken: "a", refreshToken: "r", expiresIn: 600 }));

    const outcome = await loginWithTwoFactor(cfg, "challenge", { code: "123456" }, impl);

    expect(outcome).toEqual({ ok: true, tokens: { accessToken: "a", refreshToken: "r", expiresIn: 600 } });
    const [url, init] = (impl as unknown as { mock: { calls: [string, RequestInit][] } }).mock.calls[0];
    expect(url).toBe("http://identity.invalid/api/v1/auth/2fa/login");
    expect(JSON.parse(String(init.body))).toEqual({ challengeToken: "challenge", code: "123456", recoveryCode: null });
  });

  it("sends a recovery code in its own field, never both", async () => {
    const impl = fetchOf(json(200, { accessToken: "a" }));

    await loginWithTwoFactor(cfg, "challenge", { recoveryCode: "abc" }, impl);

    const [, init] = (impl as unknown as { mock: { calls: [string, RequestInit][] } }).mock.calls[0];
    expect(JSON.parse(String(init.body))).toEqual({ challengeToken: "challenge", code: null, recoveryCode: "abc" });
  });

  it("tells a wrong code, a dead challenge and a lockout apart by authservice's 401 texts", async () => {
    const answer = (error: string) => loginWithTwoFactor(cfg, "c", { code: "1" }, fetchOf(json(401, { error })));

    expect(await answer("That code is not valid.")).toEqual({ ok: false, error: "invalid_code" });
    expect(await answer("Invalid or expired challenge. Start the sign-in again.")).toEqual({ ok: false, error: "challenge_expired" });
    expect(await answer("Account is temporarily locked after too many failed attempts.")).toEqual({ ok: false, error: "locked" });
  });

  it("maps 429 and failures to their own outcomes and never to success", async () => {
    expect(await loginWithTwoFactor(cfg, "c", { code: "1" }, fetchOf(json(429, {})))).toEqual({ ok: false, error: "rate_limited" });
    expect(await loginWithTwoFactor(cfg, "c", { code: "1" }, fetchOf(json(500, {})))).toEqual({ ok: false, error: "identity_unavailable" });
    expect(await loginWithTwoFactor(cfg, "c", { code: "1" }, fetchOf(new TypeError("down")))).toEqual({ ok: false, error: "identity_unavailable" });
    expect(await loginWithTwoFactor(cfg, "c", { code: "1" }, fetchOf(json(200, {})))).toEqual({ ok: false, error: "identity_unavailable" });
  });
});

describe("registration and email verification", () => {
  it("reads the versions a registration must accept, and is null when unavailable", async () => {
    expect(await fetchConsentVersions(cfg, fetchOf(json(200, { terms: "t1", privacy: "p1", cookies: "c1" })))).toEqual({ terms: "t1", privacy: "p1" });
    expect(await fetchConsentVersions(cfg, fetchOf(json(200, { terms: 1 })))).toBeNull();
    expect(await fetchConsentVersions(cfg, fetchOf(json(500, {})))).toBeNull();
  });

  it("registers with the versions authservice has in force and tells 200 from 202", async () => {
    const input = { email: "a@example.invalid", password: "long-enough", terms: "t1", privacy: "p1", locale: "en" };
    const impl = fetchOf(json(202, {}));

    expect(await registerAccount(cfg, input, impl)).toEqual({ ok: true, verificationRequired: true });
    expect(await registerAccount(cfg, input, fetchOf(json(200, { accessToken: "never-used" })))).toEqual({ ok: true, verificationRequired: false });
    const [, init] = (impl as unknown as { mock: { calls: [string, RequestInit][] } }).mock.calls[0];
    expect(JSON.parse(String(init.body))).toMatchObject({ acceptedTermsVersion: "t1", acceptedPrivacyVersion: "p1" });
  });

  it("passes on authservice's validation messages (rules, never the submitted values), bounded", async () => {
    const outcome = await registerAccount(cfg, { email: "a@example.invalid", password: "x", terms: "t", privacy: "p" }, fetchOf(json(400, { errors: ["Passwords must be at least 8 characters.", 5] })));

    expect(outcome).toEqual({ ok: false, error: "invalid", messages: ["Passwords must be at least 8 characters."] });
  });

  it("maps verification outcomes without becoming an account-existence oracle", async () => {
    expect(await verifyEmail(cfg, "a@example.invalid", "t", fetchOf(json(200, {})))).toEqual({ ok: true });
    expect(await verifyEmail(cfg, "a@example.invalid", "t", fetchOf(json(400, {})))).toEqual({ ok: false, error: "invalid" });
    expect(await verifyEmail(cfg, "a@example.invalid", "t", fetchOf(json(429, {})))).toEqual({ ok: false, error: "rate_limited" });
    expect(await resendVerification(cfg, "a@example.invalid", fetchOf(json(200, {})))).toEqual({ ok: true });
  });
});

describe("export", () => {
  it("returns authservice's response untouched for the caller to stream, and null when unreachable", async () => {
    const response = await fetchExport(cfg, "t", fetchOf(json(200, { account: {} })));

    expect(response?.status).toBe(200);
    expect(await fetchExport(cfg, "t", fetchOf(new TypeError("down")))).toBeNull();
  });
});
