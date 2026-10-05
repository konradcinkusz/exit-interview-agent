// The standard header set every frontend ships (SECURITY-REVIEW §4). The static part is applied by next.config.ts to every
// response; the Content-Security-Policy is per request, because it carries a nonce (ADR-0047), and is built here and set
// by the edge gate (proxy.ts). Both halves are asserted by unit tests and by the browser suite against the production artifact.
//
// `connect-src 'self'` is the point of the BFF: the browser talks to its own origin only. Development additionally needs
// `unsafe-eval` for React's debugging aids and relaxes styles, because the dev server injects them through JavaScript.

export interface Header {
  key: string;
  value: string;
}

/** A fresh, unguessable value per request: 128 random bits, base64. Never reused, never logged. */
export function generateNonce(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  let binary = "";
  for (const b of bytes) binary += String.fromCharCode(b);
  return btoa(binary);
}

export function contentSecurityPolicy(nonce: string, production: boolean): string {
  // Inline scripts need the per-request nonce; everything else must be a file served from this origin. There is no
  // 'unsafe-inline' for scripts and no 'strict-dynamic': with it, any script element that JavaScript creates is trusted,
  // which is exactly what an injected snippet does (the browser suite asserts such an injection is refused).
  const script = production ? `script-src 'self' 'nonce-${nonce}'` : `script-src 'self' 'nonce-${nonce}' 'unsafe-eval'`;
  // Styles: the app ships stylesheets as same-origin files and writes no style attributes, so 'self' is enough in production.
  // Development injects <style> through JavaScript (hot reload), which only 'unsafe-inline' allows.
  const style = production ? "style-src 'self'" : "style-src 'self' 'unsafe-inline'";
  return [
    "default-src 'self'",
    script,
    style,
    "img-src 'self' data:",
    "connect-src 'self'",
    "font-src 'self'",
    "frame-ancestors 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "object-src 'none'",
  ].join("; ");
}

export function securityHeaders(): Header[] {
  return [
    { key: "X-Frame-Options", value: "DENY" },
    { key: "X-Content-Type-Options", value: "nosniff" },
    { key: "Referrer-Policy", value: "no-referrer" },
    { key: "Permissions-Policy", value: "camera=(), microphone=(), geolocation=(), payment=()" },
    { key: "Cross-Origin-Opener-Policy", value: "same-origin" },
    { key: "Cross-Origin-Resource-Policy", value: "same-origin" },
  ];
}
