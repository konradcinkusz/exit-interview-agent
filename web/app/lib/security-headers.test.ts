import { describe, expect, it } from "vitest";
import { securityHeaders } from "./security-headers";

describe("securityHeaders", () => {
  const byKey = (production: boolean) => Object.fromEntries(securityHeaders(production).map((h) => [h.key, h.value]));

  it("ships the five-header set", () => {
    expect(Object.keys(byKey(true)).sort()).toEqual(
      ["Content-Security-Policy", "Permissions-Policy", "Referrer-Policy", "X-Content-Type-Options", "X-Frame-Options"],
    );
  });

  it("confines the browser to its own origin and refuses framing", () => {
    const csp = byKey(true)["Content-Security-Policy"];

    expect(csp).toContain("connect-src 'self'");
    expect(csp).toContain("frame-ancestors 'none'");
    expect(csp).toContain("form-action 'self'");
    expect(csp).toContain("object-src 'none'");
    expect(byKey(true)["Referrer-Policy"]).toBe("no-referrer"); // the consent and login URLs carry ?redirect=
  });

  it("allows unsafe-eval only outside production", () => {
    expect(byKey(true)["Content-Security-Policy"]).not.toContain("unsafe-eval");
    expect(byKey(false)["Content-Security-Policy"]).toContain("unsafe-eval");
  });
});
