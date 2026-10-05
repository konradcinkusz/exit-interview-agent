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
// Refresh tokens rotate and are single-use like authservice's: presenting a consumed one counts as reuse and revokes
// the account's sessions. GET /__test/stats?email= reports what the stub observed.
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

http.createServer(async (req, res) => {
  const url = new URL(req.url ?? "/", `http://localhost:${port}`);
  if (url.pathname === "/health") return json(res, 200, { status: "ok" });
  if (url.pathname === "/.well-known/jwks.json") return json(res, 200, { keys: [jwk] });
  // Test-only: lets a spec obtain a validly signed token for an arbitrary subject.
  if (url.pathname === "/__test/token") return json(res, 200, { token: await mint(url.searchParams.get("sub") ?? "account-e2e") });
  if (url.pathname === "/__test/stats") {
    const a = accounts.get(url.searchParams.get("email") ?? "");
    return a ? json(res, 200, { ...a, currentRefresh: undefined }) : json(res, 404, { error: "unknown account" });
  }
  if (url.pathname === "/__test/revoke" && req.method === "POST") {
    const a = accounts.get(url.searchParams.get("email") ?? "");
    if (a) a.revoked = true;
    return json(res, a ? 200 : 404, {});
  }

  if (url.pathname === "/api/v1/auth/login" && req.method === "POST") {
    const { email, password } = JSON.parse((await readBody(req)) || "{}");
    const a = account(email ?? "");
    if (!a || a.deleted || password !== FAKE_PASSWORD) return json(res, 401, { error: "Invalid email or password" });
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
    if (password !== FAKE_PASSWORD) return json(res, 400, { error: "Invalid password" });
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
