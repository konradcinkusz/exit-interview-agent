// The standard header set every frontend ships (SECURITY-REVIEW §4). One list, used by next.config.ts and asserted by
// a unit test and by the browser suite against the production artifact.
//
// The CSP allows inline scripts and styles because Next.js emits inline bootstrap scripts and this app does not use
// per-request nonces; everything else is same-origin. `connect-src 'self'` is the point of the BFF: the browser talks to its
// own origin only. Development additionally needs `unsafe-eval` for React's debugging aids.

export interface Header {
  key: string;
  value: string;
}

export function securityHeaders(production: boolean): Header[] {
  const script = production ? "script-src 'self' 'unsafe-inline'" : "script-src 'self' 'unsafe-inline' 'unsafe-eval'";
  const csp = [
    "default-src 'self'",
    script,
    "style-src 'self' 'unsafe-inline'",
    "img-src 'self' data:",
    "connect-src 'self'",
    "font-src 'self'",
    "frame-ancestors 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "object-src 'none'",
  ].join("; ");
  return [
    { key: "Content-Security-Policy", value: csp },
    { key: "X-Frame-Options", value: "DENY" },
    { key: "X-Content-Type-Options", value: "nosniff" },
    { key: "Referrer-Policy", value: "no-referrer" },
    { key: "Permissions-Policy", value: "camera=(), microphone=(), geolocation=(), payment=()" },
  ];
}
