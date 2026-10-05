/** `?redirect=` is an open redirect unless constrained: only same-origin absolute paths pass. */
export function safeRedirect(target: string | null | undefined, fallback = "/account"): string {
  if (!target || !target.startsWith("/") || target.startsWith("//") || target.startsWith("/\\")) return fallback;
  return target;
}
