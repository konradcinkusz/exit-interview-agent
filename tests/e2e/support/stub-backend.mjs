// A STUB standing in for authservice + interview-service during browser tests. It is test
// scaffolding, not a mock of product behaviour: it serves a JWKS, one fake account's login, and the
// authenticated /api/v1/me that echoes the verified subject, so the web app's real BFF code
// (login -> HttpOnly cookie -> edge gate -> proxy with bearer injection) runs unmodified.
// The key pair is generated per run and never leaves this process. The credentials below are fake
// and exist only here.
import http from "node:http";
import { SignJWT, exportJWK, generateKeyPair, jwtVerify } from "jose";

import { AUDIENCE, FAKE_EMAIL, FAKE_PASSWORD, ISSUER } from "./constants.mjs";

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

http.createServer(async (req, res) => {
  const url = new URL(req.url ?? "/", `http://localhost:${port}`);
  if (url.pathname === "/health") return json(res, 200, { status: "ok" });
  if (url.pathname === "/.well-known/jwks.json") return json(res, 200, { keys: [jwk] });
  // Test-only: lets a spec obtain a validly signed token for an arbitrary subject.
  if (url.pathname === "/__test/token") return json(res, 200, { token: await mint(url.searchParams.get("sub") ?? "account-e2e") });
  if (url.pathname === "/api/v1/auth/login" && req.method === "POST") {
    const { email, password } = JSON.parse((await readBody(req)) || "{}");
    if (email !== FAKE_EMAIL || password !== FAKE_PASSWORD) return json(res, 401, { error: "Invalid email or password" });
    return json(res, 200, { accessToken: await mint("account-e2e"), refreshToken: "refresh-e2e", expiresIn: 600, tokenType: "Bearer" });
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
