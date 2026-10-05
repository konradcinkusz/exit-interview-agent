import { createRemoteJWKSet, jwtVerify, type JWTPayload, type JWTVerifyGetKey, type CryptoKey } from "jose";
import { identityConfigured, type AuthConfig } from "./runtime-config";

// Middleware verifies, it does not decode (FRONTEND-BFF §4): signature against the identity
// service's JWKS, plus issuer and audience, asymmetric algorithms only. The API behind the proxy
// still enforces its own authorization: this gate is UX, the services are the boundary.

type KeyInput = CryptoKey | Uint8Array | JWTVerifyGetKey;

const jwksCache = new Map<string, JWTVerifyGetKey>();

function remoteKeys(authUrl: string): JWTVerifyGetKey {
  let keys = jwksCache.get(authUrl);
  if (!keys) {
    keys = createRemoteJWKSet(new URL(`${authUrl}/.well-known/jwks.json`), { timeoutDuration: 5000 });
    jwksCache.set(authUrl, keys);
  }
  return keys;
}

/** Returns the verified payload, or null for any failure (including identity not configured). */
export async function verifyAccessToken(
  token: string | undefined,
  cfg: AuthConfig,
  keys?: KeyInput,
): Promise<JWTPayload | null> {
  if (!token || !identityConfigured(cfg)) return null;
  try {
    const options = { issuer: cfg.issuer, audience: cfg.audience, algorithms: ["RS256"], clockTolerance: 30 };
    const key = keys ?? remoteKeys(cfg.url!);
    const { payload } = await jwtVerify(token, key as never, options);
    return payload;
  } catch {
    return null;
  }
}
