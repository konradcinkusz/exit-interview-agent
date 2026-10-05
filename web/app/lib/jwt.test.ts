import { SignJWT, generateKeyPair, type JWTVerifyGetKey } from "jose";
import { beforeAll, describe, expect, it } from "vitest";
import { verifyAccessToken } from "./jwt";
import type { AuthConfig } from "./runtime-config";

const cfg: AuthConfig = { url: "http://auth.example.invalid", issuer: "ExitInterviewAgent", audience: "ExitInterviewAgent", secureCookies: true };

let privateKey: CryptoKey;
let publicKey: CryptoKey;
let otherPrivate: CryptoKey;
const keys: JWTVerifyGetKey = async () => publicKey;

const sign = (key: CryptoKey, claims: { iss?: string; aud?: string; exp?: string } = {}) =>
  new SignJWT({ sub: "account-1" })
    .setProtectedHeader({ alg: "RS256" })
    .setIssuer(claims.iss ?? cfg.issuer!)
    .setAudience(claims.aud ?? cfg.audience!)
    .setExpirationTime(claims.exp ?? "5m")
    .sign(key);

beforeAll(async () => {
  ({ privateKey, publicKey } = await generateKeyPair("RS256"));
  ({ privateKey: otherPrivate } = await generateKeyPair("RS256"));
});

describe("verifyAccessToken", () => {
  it("accepts a correctly signed token for this issuer and audience", async () => {
    expect((await verifyAccessToken(await sign(privateKey), cfg, keys))?.sub).toBe("account-1");
  });

  it("rejects a token signed by another key", async () => {
    expect(await verifyAccessToken(await sign(otherPrivate), cfg, keys)).toBeNull();
  });

  it("rejects the wrong issuer and the wrong audience", async () => {
    expect(await verifyAccessToken(await sign(privateKey, { iss: "someone-else" }), cfg, keys)).toBeNull();
    expect(await verifyAccessToken(await sign(privateKey, { aud: "other-product" }), cfg, keys)).toBeNull();
  });

  it("rejects an expired token", async () => {
    expect(await verifyAccessToken(await sign(privateKey, { exp: "-10m" }), cfg, keys)).toBeNull();
  });

  it("rejects an unsigned token that merely decodes to the right claims (decode is not verify)", async () => {
    const enc = (o: object) => Buffer.from(JSON.stringify(o)).toString("base64url");
    const forged = `${enc({ alg: "none" })}.${enc({ sub: "x", iss: cfg.issuer, aud: cfg.audience, exp: 9999999999 })}.`;
    expect(await verifyAccessToken(forged, cfg, keys)).toBeNull();
  });

  it("rejects a symmetrically signed token (HS256) even with matching claims", async () => {
    const hs = await new SignJWT({ sub: "x" })
      .setProtectedHeader({ alg: "HS256" })
      .setIssuer(cfg.issuer!).setAudience(cfg.audience!).setExpirationTime("5m")
      .sign(new TextEncoder().encode("0123456789abcdef0123456789abcdef"));
    expect(await verifyAccessToken(hs, cfg, keys)).toBeNull();
  });

  it("rejects everything when identity is not configured, and a missing token", async () => {
    expect(await verifyAccessToken(await sign(privateKey), { secureCookies: true }, keys)).toBeNull();
    expect(await verifyAccessToken(undefined, cfg, keys)).toBeNull();
  });
});
