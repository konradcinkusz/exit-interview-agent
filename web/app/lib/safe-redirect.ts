/**
 * `?redirect=` is an open redirect unless constrained: only same-origin absolute paths pass. Without one, a person who signs in
 * goes to the interview: that is what the portal is for (docs/ux/UI-UX.md, flow 1).
 */
export function safeRedirect(target: string | null | undefined, fallback = "/interview"): string {
  if (!target || !target.startsWith("/") || target.startsWith("//") || target.startsWith("/\\")) return fallback;
  return target;
}
