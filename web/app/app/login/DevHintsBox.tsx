import type { Messages } from "@/lib/messages";
import type { DevHints } from "@/lib/runtime-config";

/**
 * The local stack's test account, so a developer can sign in without reading the AppHost source. Rendered only when `hints` is
 * not null, which happens only with DEV_HINTS=1 (lib/runtime-config.ts). Labelled as local development, in its own landmark.
 */
export function DevHintsBox({ hints, t }: { hints: DevHints | null; t: Messages["devHints"] }) {
  if (!hints) return null;
  return (
    <aside className="dev-hints" aria-labelledby="dev-hints-heading" data-testid="dev-hints">
      <h2 id="dev-hints-heading">{t.title}</h2>
      <p>{t.intro}</p>
      <dl className="facts">
        <div><dt>{t.email}</dt><dd><code data-testid="dev-hints-email">{hints.email}</code></dd></div>
        <div><dt>{t.password}</dt><dd><code data-testid="dev-hints-password">{hints.password}</code></dd></div>
      </dl>
      <p>{t.seed}</p>
    </aside>
  );
}
