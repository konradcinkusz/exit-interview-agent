import { describe, expect, it } from "vitest";
import { contentSecurityPolicy, generateNonce, securityHeaders } from "./security-headers";

describe("securityHeaders", () => {
  const byKey = Object.fromEntries(securityHeaders().map((h) => [h.key, h.value]));

  it("ships the static set (the CSP is per request)", () => {
    expect(Object.keys(byKey).sort()).toEqual([
      "Cross-Origin-Opener-Policy",
      "Cross-Origin-Resource-Policy",
      "Permissions-Policy",
      "Referrer-Policy",
      "X-Content-Type-Options",
      "X-Frame-Options",
    ]);
    expect(byKey["Referrer-Policy"]).toBe("no-referrer"); // the consent and login URLs carry ?redirect=
    expect(byKey["X-Frame-Options"]).toBe("DENY");
  });
});

describe("contentSecurityPolicy", () => {
  const nonce = "bm9uY2U=";
  const directive = (csp: string, name: string) => csp.split("; ").find((d) => d.startsWith(`${name} `)) ?? "";

  it("confines the browser to its own origin and refuses framing", () => {
    const csp = contentSecurityPolicy(nonce, true);

    expect(csp).toContain("connect-src 'self'");
    expect(csp).toContain("frame-ancestors 'none'");
    expect(csp).toContain("form-action 'self'");
    expect(csp).toContain("object-src 'none'");
    expect(csp).toContain("base-uri 'self'");
  });

  it("gates scripts on the nonce and has no unsafe-inline for scripts or styles in production", () => {
    const csp = contentSecurityPolicy(nonce, true);

    expect(directive(csp, "script-src")).toContain(`'nonce-${nonce}'`);
    expect(directive(csp, "script-src")).not.toContain("unsafe-inline");
    expect(directive(csp, "script-src")).not.toContain("unsafe-eval");
    expect(directive(csp, "style-src")).not.toContain("unsafe-inline");
  });

  it("allows eval and inline styles only outside production", () => {
    const dev = contentSecurityPolicy(nonce, false);

    expect(directive(dev, "script-src")).toContain("unsafe-eval");
    expect(directive(dev, "style-src")).toContain("unsafe-inline");
  });

  it("allows no third-party origin", () => {
    expect(contentSecurityPolicy(nonce, true)).not.toMatch(/https?:/);
  });
});

describe("generateNonce", () => {
  it("is 128 random bits, base64, and different every time", () => {
    const a = generateNonce();
    const b = generateNonce();

    expect(a).toMatch(/^[A-Za-z0-9+/]{22}==$/);
    expect(a).not.toBe(b);
  });
});
