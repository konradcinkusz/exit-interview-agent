// A STUB standing in for authservice + interview-service during browser tests. It is test
// scaffolding, not a mock of product behaviour: it serves a JWKS, fake accounts' login, rotating refresh,
// consents, logout, account deletion and the authenticated /api/v1/me that echoes the verified subject, so the
// web app's real BFF code (login -> HttpOnly cookies -> edge gate -> refresh rotation -> consent step -> proxy with
// bearer injection) runs unmodified. The key pair is generated per run and never leaves this process. Credentials
// are fake and exist only here.
//
// Accounts are created lazily from the email's local part, so every test can own an account and run in parallel:
//   persona@                 the original account (subject "account-e2e"), consent already accepted
//   consent-*@               consent NOT yet accepted at first login
//   anything-else@           consent already accepted
//   twofactor-*@             sign-in needs a second factor: code 123456, or the single-use recovery code in RECOVERY_CODE
//   unverified-*@            exists after registration but its email is not verified (verification token: vt-<local part>)
//
// CONTRACT NOTE (keep in sync). The ticket and receipt routes mirror the T5 contract of interview-service
// (src/ExitInterviewAgent.Contracts/SubmissionContracts.cs, ADR-0029, ADR-0030): status codes, the problem+json body
// {type, title, status, code} for TICKET_LIMIT and INVALID_RECEIPT_CODE, the kernel limiter body {error:"rate_limited",
// retryAfter} with a Retry-After header, the X-Receipt-Code header, 204 for every well-formed code, a 46-character
// URL-safe-base64 code whose last two bytes are the first two bytes of SHA-256 over the first 32, 43-character tickets,
// at most 3 live tickets per account. If the backend contract changes, change this file in the same pull request.
// The signals routes mirror the T10 contract (src/ExitInterviewAgent.Contracts/SignalsContracts.cs, ADR-0053..0056,
// docs/privacy/AGGREGATION.md section 8): policy `account` (strict bearer), the response shapes below (six topics always; an
// `insufficient_data` topic has no overall and no cuts; a suppressed cut has no cells; a published cut lists every band of its
// dimension as ok or none), alphabetical paging clamped like ApiExtensions.ClampPage (page >= 1, limit default 25, 1..100), a weak
// ETag per snapshot and resource, `Cache-Control: private, max-age=<seconds to the next batch, at least 60>`, `Vary: Authorization`,
// `Last-Modified` = the batch start, 304 on If-None-Match, SIGNALS_EMPLOYER_NOT_FOUND (404, byte-identical for an unknown employer
// and one below the minimum group size, with the cache header only), SIGNALS_INVALID_EMPLOYER_REF (400) and the kernel limiter's 429
// body with Retry-After. With no snapshot the list answers `{snapshot:null, employers:[], ...}` with `no-store`. The data are
// synthetic. Per-account switches (/__test/signals-config) keep parallel tests independent.
// Refresh tokens rotate and are single-use like authservice's: presenting a consumed one counts as reuse and revokes
// the account's sessions. GET /__test/stats?email= reports what the stub observed.
import { createHash, randomBytes } from "node:crypto";
import http from "node:http";
import { SignJWT, exportJWK, generateKeyPair, jwtVerify } from "jose";

import { AUDIENCE, FAKE_EMAIL, FAKE_PASSWORD, ISSUER, PRIVACY_VERSION, TERMS_VERSION } from "./constants.mjs";

const port = Number(process.env.STUB_PORT ?? 4010);
const { publicKey, privateKey } = await generateKeyPair("RS256");
const jwk = { ...(await exportJWK(publicKey)), alg: "RS256", use: "sig", kid: "e2e-key" };

const mint = (sub) =>
  new SignJWT({ sub }).setProtectedHeader({ alg: "RS256", kid: "e2e-key" })
    .setIssuer(ISSUER).setAudience(AUDIENCE).setExpirationTime("10m").sign(privateKey);

const json = (res, status, body) => {
  res.writeHead(status, { "content-type": "application/json" });
  res.end(JSON.stringify(body));
};
const readBody = (req) => new Promise((resolve) => {
  let data = "";
  req.on("data", (c) => (data += c));
  req.on("end", () => resolve(data));
});
const RECOVERY_CODE = "recovery-e2e-1";
const TWO_FACTOR_CODE = "123456";
const PROBLEM = "urn:exit-interview-agent:problem:";
const b64url = (buf) => Buffer.from(buf).toString("base64url");
const problem = (res, status, code) => {
  res.writeHead(status, { "content-type": "application/problem+json" });
  res.end(JSON.stringify({ type: PROBLEM + code.toLowerCase(), title: code, status, code }));
};
const wellFormedReceipt = (code) => {
  if (typeof code !== "string" || code.length !== 46 || !/^[A-Za-z0-9_-]+$/.test(code)) return false;
  const bytes = Buffer.from(code, "base64url");
  return bytes.length === 34 && createHash("sha256").update(bytes.subarray(0, 32)).digest().subarray(0, 2).equals(bytes.subarray(32));
};
const newReceiptCode = () => {
  const secret = randomBytes(32);
  return b64url(Buffer.concat([secret, createHash("sha256").update(secret).digest().subarray(0, 2)]));
};
/** What the stub observed about each receipt-deletion request, keyed by SHA-256 of the code (never the code itself). */
const receiptLog = [];
/** sha256(code) -> how many more times to answer 429, so a test's forced limit cannot hit another test's request. */
const receiptForceLimit = new Map();
// --- Signals fixtures (synthetic) ---------------------------------------------------------------------------------------
const TOPICS = ["onboarding", "management", "growth", "pay_vs_promises", "culture", "reason_for_leaving"];
const TENURE = ["lt_6m", "6m_1y", "1y_3y", "3y_5y", "5y_10y", "gt_10y"];
const SENIORITY = ["junior", "mid", "senior", "management"];
const FUNCTION = ["engineering", "product_design", "sales_marketing", "operations_support", "corporate_functions", "other"];
const stats = (n, mean, lower, upper, reliability, coverage, distribution = null, verification = null) => ({
  n, mean, interval: { lower, upper, level: 0.95, method: "regularised-t" }, reliability, coverage, distribution, verification,
});
const groups = (keys, counts) => keys.map((key, i) => ({ key, count: counts[i] }));
const DIST = ["low", "mid", "high"];
const VERIF = ["unchecked", "unverified", "verified"];
const cut = (dimension, bands, shown) => ({
  dimension, status: "published", cells: bands.map((band) => (shown[band] ? { band, status: "ok", stats: shown[band] } : { band, status: "none", stats: null })),
});
const withheld = (dimension) => ({ dimension, status: "suppressed", cells: [] });
const insufficient = (topic) => ({ topic, status: "insufficient_data", overall: null, cuts: [] });
const topicOf = (topic, overall, cuts) => ({ topic, status: "ok", overall, cuts });
const EMPLOYERS = new Map([
  ["demo-acme", {
    respondentsBand: "10-24",
    topics: [
      topicOf("onboarding", stats(12, 3.82, 3.4, 4.2, "moderate", "high", groups(DIST, [0, 5, 7]), groups(VERIF, [12, 0, 0])), [
        cut("tenure", TENURE, { "1y_3y": stats(6, 3.5, 2.9, 4.1, "low", "medium"), "3y_5y": stats(6, 4.1, 3.5, 4.6, "low", "high") }),
        withheld("seniority"),
        cut("function", FUNCTION, { engineering: stats(7, 3.9, 3.3, 4.4, "low", "high"), other: stats(5, 3.7, 2.8, 4.5, "low", "medium") }),
      ]),
      insufficient("management"),
      topicOf("growth", stats(30, 4.05, 3.8, 4.3, "high", "medium", null, groups(VERIF, [12, 8, 10])), [withheld("tenure"), withheld("seniority"), withheld("function")]),
      topicOf("pay_vs_promises", stats(5, 3.2, 2.3, 4.1, "low", "low", groups(DIST, [0, 5, 0]), null), [withheld("tenure"), withheld("seniority"), withheld("function")]),
      topicOf("culture", stats(14, 2.9, 2.5, 3.3, "moderate", "high", groups(DIST, [5, 9, 0]), groups(VERIF, [14, 0, 0])), [
        cut("tenure", TENURE, { "1y_3y": stats(7, 2.8, 2.2, 3.4, "low", "high"), "3y_5y": stats(7, 3.0, 2.4, 3.6, "low", "high") }),
        cut("seniority", SENIORITY, { mid: stats(14, 2.9, 2.5, 3.3, "moderate", "high") }),
        withheld("function"),
      ]),
      insufficient("reason_for_leaving"),
    ],
  }],
  ["demo-beta", {
    respondentsBand: "5-9",
    topics: TOPICS.map((t) => (t === "culture" ? topicOf(t, stats(5, 4.4, 3.8, 5, "low", "medium"), [withheld("tenure"), withheld("seniority"), withheld("function")]) : insufficient(t))),
  }],
  ["demo-gamma", {
    respondentsBand: "50+",
    topics: TOPICS.map((t) => topicOf(t, stats(60, 3.1, 2.9, 3.3, "high", "high", groups(DIST, [20, 20, 20]), groups(VERIF, [60, 0, 0])), [
      cut("tenure", TENURE, { "1y_3y": stats(30, 3.0, 2.7, 3.3, "high", "high"), "3y_5y": stats(30, 3.2, 2.9, 3.5, "high", "high") }),
      withheld("seniority"),
      withheld("function"),
    ])),
  }],
]);
// A reference no real answer would carry: band, topic and group strings that are markup. The vocabularies are closed in the real
// API; the page must not rely on that. Not listed (the list shows alphabetical refs of the others only).
const HOSTILE = "<img src=x onerror=\"window.__pwned=1\">";
EMPLOYERS.set("demo-hostile", {
  respondentsBand: HOSTILE,
  topics: [
    topicOf(HOSTILE, stats(12, 3.5, 3, 4, "moderate", "high", [{ key: HOSTILE, count: 12 }], null), [cut(HOSTILE, [HOSTILE, "1y_3y"], { [HOSTILE]: stats(6, 3.5, 3, 4, "low", "high") })]),
    ...TOPICS.slice(1).map(insufficient),
  ],
});
// 22 more employers so that the list needs a second page at the default page size of this UI (20).
for (let i = 1; i <= 22; i += 1) {
  EMPLOYERS.set(`demo-filler-${String(i).padStart(2, "0")}`, {
    respondentsBand: "5-9",
    topics: TOPICS.map((t) => (t === "onboarding" ? topicOf(t, stats(5, 3.5, 2.6, 4.4, "low", "medium"), [withheld("tenure"), withheld("seniority"), withheld("function")]) : insufficient(t))),
  });
}
const LISTED = [...EMPLOYERS.keys()].filter((k) => k !== "demo-hostile").sort((a, b) => (a < b ? -1 : a > b ? 1 : 0)); // ordinal, like the service
const SIGNALS_SEQ = 1;
const batchStart = () => {
  const d = new Date();
  d.setUTCHours(0, 0, 0, 0);
  return d;
};
const snapshotInfo = () => ({ generatedAt: batchStart().toISOString().replace(".000Z", "+00:00"), publicationIntervalHours: 24, rulesVersion: "1", minimumGroupSize: 5, deletionsAppearAtNextPublication: true });
const signalsCacheControl = () => `private, max-age=${Math.max(60, Math.floor((batchStart().getTime() + 24 * 3600_000 - Date.now()) / 1000))}`;
const isEmployerRef = (v) => typeof v === "string" && v.length >= 3 && v.length <= 64 && /^[a-z0-9]+(-[a-z0-9]+)*$/.test(v);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/** @type {Map<string, any>} keyed by email */
const accounts = new Map();
/** refresh token -> email, including consumed ones, so reuse is recognisable */
const refreshOwners = new Map();

function account(email) {
  if (!/^[a-z0-9-]+@example\.invalid$/.test(email)) return null;
  let a = accounts.get(email);
  if (!a) {
    const local = email.split("@")[0];
    a = {
      email,
      subject: email === FAKE_EMAIL ? "account-e2e" : `account-${local}`,
      password: FAKE_PASSWORD,
      twoFactor: local.startsWith("twofactor"),
      recoveryUsed: false,
      secondFactorFailures: 0,
      challenge: null,
      verified: !local.startsWith("unverified"),
      tickets: [],
      mintTimes: [],
      ticketTtlSeconds: null,
      forceRateLimit: false,
      signals: { requests: 0, conditional: 0, notModified: 0, limitNext: 0, retryAfter: 2, empty: false },
      consentAccepted: !local.startsWith("consent"),
      lastLocale: null,
      currentRefresh: null,
      issued: 0,
      refreshCalls: 0,
      reuseDetected: 0,
      logouts: 0,
      revoked: false,
      deleted: false,
    };
    accounts.set(email, a);
  }
  return a;
}

const issuePair = async (a) => {
  a.issued += 1;
  a.currentRefresh = `rt-${a.email.split("@")[0]}-${a.issued}`;
  refreshOwners.set(a.currentRefresh, a.email);
  return { accessToken: await mint(a.subject), refreshToken: a.currentRefresh, expiresIn: 600, tokenType: "Bearer" };
};

const consentStatus = (a) => ({
  terms: { requiredVersion: TERMS_VERSION, acceptedVersion: a.consentAccepted ? TERMS_VERSION : null, acceptedAt: null, accepted: a.consentAccepted },
  privacy: { requiredVersion: PRIVACY_VERSION, acceptedVersion: a.consentAccepted ? PRIVACY_VERSION : null, acceptedAt: null, accepted: a.consentAccepted },
  cookies: { requiredVersion: "2026-01-01", acceptedVersion: null, acceptedAt: null, accepted: false },
  requiresConsent: !a.consentAccepted,
});

/** Verifies the bearer the way an API would, but tolerates expiry: these endpoints are about the account, not freshness. */
async function bearerAccount(req) {
  const bearer = /^Bearer (.+)$/.exec(req.headers.authorization ?? "")?.[1];
  if (!bearer) return null;
  try {
    const { payload } = await jwtVerify(bearer, publicKey, { issuer: ISSUER, audience: AUDIENCE, algorithms: ["RS256"], clockTolerance: "1h" });
    return [...accounts.values()].find((a) => a.subject === payload.sub) ?? null;
  } catch {
    return null;
  }
}

/** Verifies the bearer strictly (expiry enforced), like the interview-service's `account` policy. */
async function strictBearerAccount(req) {
  const bearer = /^Bearer (.+)$/.exec(req.headers.authorization ?? "")?.[1];
  if (!bearer) return null;
  try {
    const { payload } = await jwtVerify(bearer, publicKey, { issuer: ISSUER, audience: AUDIENCE, algorithms: ["RS256"] });
    return [...accounts.values()].find((a) => a.subject === payload.sub) ?? null;
  } catch {
    return null;
  }
}

http.createServer(async (req, res) => {
  const url = new URL(req.url ?? "/", `http://localhost:${port}`);
  if (url.pathname === "/health") return json(res, 200, { status: "ok" });
  if (url.pathname === "/.well-known/jwks.json") return json(res, 200, { keys: [jwk] });
  // Test-only: lets a spec obtain a validly signed token for an arbitrary subject.
  if (url.pathname === "/__test/token") return json(res, 200, { token: await mint(url.searchParams.get("sub") ?? "account-e2e") });
  if (url.pathname === "/__test/stats") {
    const a = accounts.get(url.searchParams.get("email") ?? "");
    return a ? json(res, 200, { ...a, currentRefresh: undefined, challenge: undefined, password: undefined, signals: undefined, tickets: a.tickets.length }) : json(res, 404, { error: "unknown account" });
  }
  if (url.pathname === "/__test/revoke" && req.method === "POST") {
    const a = accounts.get(url.searchParams.get("email") ?? "");
    if (a) a.revoked = true;
    return json(res, a ? 200 : 404, {});
  }

  if (url.pathname === "/__test/receipt-code") return json(res, 200, { code: newReceiptCode() });
  if (url.pathname === "/__test/receipt-log") {
    const h = url.searchParams.get("hash");
    return json(res, 200, receiptLog.filter((e) => !h || e.codeHash === h));
  }
  if (url.pathname === "/__test/receipt-limit" && req.method === "POST") {
    receiptForceLimit.set(url.searchParams.get("hash") ?? "", Number(url.searchParams.get("count") ?? 1));
    return json(res, 200, {});
  }
  if (url.pathname === "/__test/ticket-config" && req.method === "POST") {
    const a = accounts.get(url.searchParams.get("email") ?? "");
    if (!a) return json(res, 404, {});
    if (url.searchParams.has("ttl")) a.ticketTtlSeconds = Number(url.searchParams.get("ttl"));
    if (url.searchParams.has("rateLimit")) a.forceRateLimit = url.searchParams.get("rateLimit") === "1";
    return json(res, 200, {});
  }
  if (url.pathname === "/__test/expire-challenge" && req.method === "POST") {
    const a = accounts.get(url.searchParams.get("email") ?? "");
    if (a) a.challenge = null;
    return json(res, a ? 200 : 404, {});
  }

  if (url.pathname === "/api/v1/tickets" && req.method === "POST") {
    const a = await strictBearerAccount(req);
    if (!a) return json(res, 401, { error: "no bearer" });
    const now = Date.now();
    a.tickets = a.tickets.filter((t) => t.expiresAt > now);
    a.mintTimes = a.mintTimes.filter((t) => t > now - 3_600_000);
    if (a.forceRateLimit || a.mintTimes.length >= 10) {
      res.writeHead(429, { "content-type": "application/json", "retry-after": "60" });
      return res.end(JSON.stringify({ error: "rate_limited", retryAfter: 60 }));
    }
    if (a.tickets.length >= 3) return problem(res, 429, "TICKET_LIMIT");
    // The service rounds the expiry UP to a 5-minute step (ADR-0030); a test may set an exact short life instead.
    const step = 300_000;
    const expiresAt = a.ticketTtlSeconds != null ? now + a.ticketTtlSeconds * 1000 : Math.ceil((now + 900_000) / step) * step;
    const ticket = b64url(randomBytes(32));
    a.tickets.push({ ticket, expiresAt });
    a.mintTimes.push(now);
    res.writeHead(201, { "content-type": "application/json" });
    return res.end(JSON.stringify({ ticket, expiresAt: new Date(expiresAt).toISOString() }));
  }

  if (url.pathname === "/__test/signals-config" && req.method === "POST") {
    const a = accounts.get(url.searchParams.get("email") ?? "");
    if (!a) return json(res, 404, {});
    if (url.searchParams.has("limit")) a.signals.limitNext = Number(url.searchParams.get("limit"));
    if (url.searchParams.has("retryAfter")) a.signals.retryAfter = Number(url.searchParams.get("retryAfter"));
    if (url.searchParams.has("empty")) a.signals.empty = url.searchParams.get("empty") === "1";
    return json(res, 200, {});
  }
  if (url.pathname === "/__test/signals-stats") {
    const a = accounts.get(url.searchParams.get("email") ?? "");
    return a ? json(res, 200, a.signals) : json(res, 404, {});
  }

  if (url.pathname.startsWith("/api/v1/signals/employers") && req.method === "GET") {
    const a = await strictBearerAccount(req);
    if (!a) return json(res, 401, { error: "no bearer" });
    a.signals.requests += 1;
    const conditional = req.headers["if-none-match"];
    if (conditional) a.signals.conditional += 1;
    if (a.signals.limitNext > 0) {
      a.signals.limitNext -= 1;
      res.writeHead(429, { "content-type": "application/json", "retry-after": String(a.signals.retryAfter) });
      return res.end(JSON.stringify({ error: "rate_limited", retryAfter: a.signals.retryAfter }));
    }
    const cached = (etagKey) => {
      const etag = `W/"s${SIGNALS_SEQ}-${etagKey}"`;
      const headers = {
        "cache-control": signalsCacheControl(), vary: "Authorization", etag, "last-modified": batchStart().toUTCString(),
      };
      const matches = String(conditional ?? "").split(",").map((v) => v.trim()).some((v) => v === etag || v === "*");
      return { headers, matches };
    };
    const ref = decodeURIComponent(url.pathname.slice("/api/v1/signals/employers".length).replace(/^\//, ""));
    if (url.pathname === "/api/v1/signals/employers" || url.pathname === "/api/v1/signals/employers/") {
      const page = Math.max(1, Number.parseInt(url.searchParams.get("page") ?? "", 10) || 1);
      const limit = Math.min(100, Math.max(1, Number.parseInt(url.searchParams.get("limit") ?? "", 10) || 25));
      if (a.signals.empty) {
        res.writeHead(200, { "content-type": "application/json", "cache-control": "no-store" });
        return res.end(JSON.stringify({ snapshot: null, employers: [], page, limit, total: 0 }));
      }
      const { headers, matches } = cached(`p${page}l${limit}`);
      if (matches) {
        a.signals.notModified += 1;
        res.writeHead(304, headers);
        return res.end();
      }
      res.writeHead(200, { "content-type": "application/json", ...headers });
      return res.end(JSON.stringify({ snapshot: snapshotInfo(), employers: LISTED.slice((page - 1) * limit, page * limit), page, limit, total: LISTED.length }));
    }
    if (!isEmployerRef(ref)) return problem(res, 400, "SIGNALS_INVALID_EMPLOYER_REF");
    const employer = a.signals.empty ? undefined : EMPLOYERS.get(ref);
    if (!employer) {
      // The same body and headers whether the employer is unknown or has no displayable cell (AGGREGATION R10).
      res.writeHead(404, { "content-type": "application/problem+json", "cache-control": a.signals.empty ? "no-store" : signalsCacheControl() });
      return res.end(JSON.stringify({ type: `${PROBLEM}signals_employer_not_found`, title: "SIGNALS_EMPLOYER_NOT_FOUND", status: 404, code: "SIGNALS_EMPLOYER_NOT_FOUND" }));
    }
    const { headers, matches } = cached(ref);
    if (matches) {
      a.signals.notModified += 1;
      res.writeHead(304, headers);
      return res.end();
    }
    res.writeHead(200, { "content-type": "application/json", ...headers });
    return res.end(JSON.stringify({ snapshot: snapshotInfo(), employerRef: ref, respondentsBand: employer.respondentsBand, topics: employer.topics }));
  }

  if (url.pathname === "/api/v1/receipts" && req.method === "DELETE") {
    // Anonymous. Records what the request carried, so specs can assert nothing but the header and no account link.
    const code = req.headers["x-receipt-code"];
    const codeHash = createHash("sha256").update(String(code ?? "")).digest("hex");
    receiptLog.push({
      codeHash,
      authorization: Boolean(req.headers.authorization),
      cookie: Boolean(req.headers.cookie),
      search: url.search,
      headerNames: Object.keys(req.headers).filter((n) => n.startsWith("x-")).sort(),
    });
    if ((receiptForceLimit.get(codeHash) ?? 0) > 0) {
      receiptForceLimit.set(codeHash, receiptForceLimit.get(codeHash) - 1);
      res.writeHead(429, { "content-type": "application/json", "retry-after": "30" });
      return res.end(JSON.stringify({ error: "rate_limited", retryAfter: 30 }));
    }
    if (!wellFormedReceipt(code)) return problem(res, 400, "INVALID_RECEIPT_CODE");
    res.writeHead(204);
    return res.end();
  }

  if (url.pathname === "/api/v1/auth/consents/versions" && req.method === "GET") {
    return json(res, 200, { terms: TERMS_VERSION, privacy: PRIVACY_VERSION, cookies: "2026-01-01" });
  }
  if (url.pathname === "/api/v1/auth/register" && req.method === "POST") {
    const { email, password, acceptedTermsVersion, acceptedPrivacyVersion } = JSON.parse((await readBody(req)) || "{}");
    if (acceptedTermsVersion !== TERMS_VERSION || acceptedPrivacyVersion !== PRIVACY_VERSION) {
      return json(res, 400, { errors: ["You must accept the current Terms of Use and Privacy Policy to register."] });
    }
    if (typeof password !== "string" || password.length < 8) return json(res, 400, { errors: ["Passwords must be at least 8 characters."] });
    const existing = accounts.get(email ?? "");
    if (existing) return json(res, 400, { errors: ["Username is already taken."] });
    const a = account(email ?? "");
    if (!a) return json(res, 400, { errors: ["Invalid email format"] });
    a.password = password;
    a.consentAccepted = true; // registration records the acceptance
    if (!a.verified) return json(res, 202, { message: "Check your email", email });
    return json(res, 200, await issuePair(a));
  }
  if (url.pathname === "/api/v1/auth/verify-email" && req.method === "POST") {
    const { email, token } = JSON.parse((await readBody(req)) || "{}");
    const a = accounts.get(email ?? "");
    if (!a || token !== `vt-${a.email.split("@")[0]}`) return json(res, 400, { error: "Invalid or expired verification token." });
    a.verified = true;
    return json(res, 200, { message: "Email address verified. You can now sign in." });
  }
  if (url.pathname === "/api/v1/auth/resend-verification" && req.method === "POST") {
    return json(res, 200, { message: "If that address needs verification, a new link has been sent." });
  }
  if (url.pathname === "/api/v1/auth/export" && req.method === "GET") {
    const a = await bearerAccount(req);
    if (!a) return json(res, 401, { error: "no bearer" });
    res.writeHead(200, { "content-type": "application/json", "content-disposition": 'attachment; filename="authservice-export.json"' });
    return res.end(JSON.stringify({ account: { id: a.subject, emailConfirmed: a.verified, twoFactorEnabled: a.twoFactor }, consents: [], sessions: [] }));
  }
  if (url.pathname === "/api/v1/auth/2fa/login" && req.method === "POST") {
    const { challengeToken, code, recoveryCode } = JSON.parse((await readBody(req)) || "{}");
    const a = [...accounts.values()].find((x) => x.challenge && x.challenge === challengeToken);
    if (!a || a.deleted) return json(res, 401, { error: "Invalid or expired challenge. Start the sign-in again." });
    if (a.secondFactorFailures >= 5) return json(res, 401, { error: "Account is temporarily locked after too many failed attempts." });
    if (!code && !recoveryCode) return json(res, 400, { error: "Provide either an authenticator code or a recovery code." });
    const ok = code ? code === TWO_FACTOR_CODE : recoveryCode === RECOVERY_CODE && !a.recoveryUsed;
    if (!ok) {
      a.secondFactorFailures += 1;
      return json(res, 401, { error: "That code is not valid." });
    }
    if (recoveryCode) a.recoveryUsed = true;
    a.challenge = null;
    a.secondFactorFailures = 0;
    a.revoked = false;
    return json(res, 200, await issuePair(a));
  }

  if (url.pathname === "/api/v1/auth/login" && req.method === "POST") {
    const { email, password } = JSON.parse((await readBody(req)) || "{}");
    const a = account(email ?? "");
    if (!a || a.deleted || password !== a.password) return json(res, 401, { error: "Invalid email or password" });
    if (!a.verified) return json(res, 403, { error: "Email address has not been verified.", emailVerificationRequired: true });
    if (a.twoFactor) {
      a.challenge = `ch-${randomBytes(12).toString("hex")}`;
      return json(res, 200, { requiresTwoFactor: true, challengeToken: a.challenge, expiresIn: 300 });
    }
    a.revoked = false;
    return json(res, 200, await issuePair(a));
  }
  if (url.pathname === "/api/v1/auth/refresh" && req.method === "POST") {
    const { refreshToken } = JSON.parse((await readBody(req)) || "{}");
    await sleep(150); // wide enough that parallel callers overlap, as they do in a browser
    const owner = refreshOwners.get(refreshToken);
    const a = owner ? accounts.get(owner) : null;
    if (!a) return json(res, 401, { error: "Invalid or expired refresh token" });
    a.refreshCalls += 1;
    if (refreshToken !== a.currentRefresh) {
      a.reuseDetected += 1; // authservice treats this as theft and revokes the family
      a.revoked = true;
      return json(res, 401, { error: "Invalid or expired refresh token" });
    }
    if (a.revoked || a.deleted) return json(res, 401, { error: "Invalid or expired refresh token" });
    return json(res, 200, await issuePair(a));
  }
  if (url.pathname === "/api/v1/auth/consents") {
    const a = await bearerAccount(req);
    if (!a) return json(res, 401, { error: "no bearer" });
    if (req.method === "POST") {
      const body = JSON.parse((await readBody(req)) || "{}");
      if (body.acceptedTerms === true && body.acceptedPrivacy === true) {
        a.consentAccepted = true;
        a.lastLocale = body.locale ?? null;
      }
    }
    return json(res, 200, consentStatus(a));
  }
  if (url.pathname === "/api/v1/auth/logout" && req.method === "POST") {
    const a = await bearerAccount(req);
    if (!a) return json(res, 401, { error: "no bearer" });
    a.logouts += 1;
    a.revoked = true;
    return json(res, 200, { message: "Logged out successfully" });
  }
  if (url.pathname === "/api/v1/auth/account" && req.method === "DELETE") {
    const a = await bearerAccount(req);
    if (!a) return json(res, 401, { error: "no bearer" });
    const { password, confirmation } = JSON.parse((await readBody(req)) || "{}");
    if (confirmation !== "DELETE") return json(res, 400, { error: "Confirmation text must be 'DELETE'" });
    if (!password) return json(res, 400, { error: "Password is required" });
    if (password !== a.password) return json(res, 400, { error: "Invalid password" });
    a.deleted = true;
    a.revoked = true;
    return json(res, 200, { message: "Account deleted successfully" });
  }
  if (url.pathname === "/api/v1/me") {
    const bearer = /^Bearer (.+)$/.exec(req.headers.authorization ?? "")?.[1];
    if (!bearer) return json(res, 401, { error: "no bearer" });
    try {
      const { payload } = await jwtVerify(bearer, publicKey, { issuer: ISSUER, audience: AUDIENCE, algorithms: ["RS256"] });
      return json(res, 200, { subject: payload.sub });
    } catch {
      return json(res, 401, { error: "bad token" });
    }
  }
  json(res, 404, { error: "not found" });
}).listen(port, "localhost", () => console.log(`stub backend on :${port}`));
