// A small set of inline SVG icons, drawn for this project on a 24-unit grid with a 1.75 stroke: no icon font, no third-party
// request, and they take the text colour (currentColor). Decorative by default (aria-hidden); the text beside an icon carries the
// meaning.

const PATHS = {
  chat: "M4 5.5A2.5 2.5 0 0 1 6.5 3h11A2.5 2.5 0 0 1 20 5.5v8a2.5 2.5 0 0 1-2.5 2.5H10l-4.5 4v-4h0A1.5 1.5 0 0 1 4 14.5z M8 8.5h8 M8 12h5",
  shield: "M12 3l7 3v5.5c0 4.4-3 8-7 9.5-4-1.5-7-5.1-7-9.5V6z M9 12l2.2 2.2L15.5 10",
  clock: "M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18z M12 7.5V12l3 2",
  trash: "M4 7h16 M9.5 7V4.5h5V7 M6.5 7l1 13h9l1-13 M10.5 11v5.5 M13.5 11v5.5",
  file: "M6 3h8l4 4v14H6z M14 3v4h4 M9 12h6 M9 15.5h6 M9 8.5h2",
  user: "M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8z M4.5 20.5c1.2-3.6 4-5.5 7.5-5.5s6.3 1.9 7.5 5.5",
  spark: "M12 3v4 M12 17v4 M3 12h4 M17 12h4 M6 6l2.5 2.5 M15.5 15.5L18 18 M18 6l-2.5 2.5 M8.5 15.5L6 18",
  check: "M5 12.5l4.5 4.5L19 7.5",
  arrow: "M5 12h14 M13 6l6 6-6 6",
  download: "M12 4v11 M7 10.5l5 5 5-5 M5 20h14",
  copy: "M9 9h11v11H9z M5 15H4V4h11v1",
  lock: "M6 11h12v9H6z M8.5 11V8a3.5 3.5 0 0 1 7 0v3",
  eyeOff: "M3 3l18 18 M10.6 6.1A9.7 9.7 0 0 1 12 6c5 0 8.5 4.5 9.5 6-.4.7-1.3 1.9-2.6 3.1 M6.3 7.7C4.4 9 3.1 10.8 2.5 12c1 1.5 4.5 6 9.5 6 1.5 0 2.9-.4 4.1-1 M9.9 9.9a3 3 0 0 0 4.2 4.2",
  info: "M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18z M12 11v5.5 M12 7.75v.5",
  alert: "M12 3.5l9.5 16.5h-19z M12 10v4.5 M12 17.25v.5",
  globe: "M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18z M3.5 9h17 M3.5 15h17 M12 3c2.3 2.5 3.5 5.5 3.5 9s-1.2 6.5-3.5 9c-2.3-2.5-3.5-5.5-3.5-9S9.7 5.5 12 3z",
  flag: "M5 21V4 M5 4.5c4-2 6 2 10 0s4 0 4 0v9s-1-1.5-4 0-6-2-10 0",
} as const;

export type IconName = keyof typeof PATHS;

export function Icon({ name, size = 20, className, label }: { name: IconName; size?: number; className?: string; label?: string }) {
  return (
    <svg
      className={className ? `icon ${className}` : "icon"}
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.75}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden={label ? undefined : true}
      role={label ? "img" : undefined}
      aria-label={label}
      focusable="false"
    >
      <path d={PATHS[name]} />
    </svg>
  );
}

/** The product mark: a speech bubble opening onto a door frame, in the accent colour. Decorative; the name follows it as text. */
export function BrandMark({ size = 28 }: { size?: number }) {
  return (
    <svg className="brand-mark" width={size} height={size} viewBox="0 0 32 32" aria-hidden="true" focusable="false">
      <rect x="1" y="1" width="30" height="30" rx="9" fill="var(--accent)" />
      <path d="M9 9.5h14a1.5 1.5 0 0 1 1.5 1.5v8a1.5 1.5 0 0 1-1.5 1.5h-7.5L11 24v-3.5H9A1.5 1.5 0 0 1 7.5 19v-8A1.5 1.5 0 0 1 9 9.5z" fill="var(--on-accent)" />
      <path d="M12 13.5h8 M12 16.5h5" stroke="var(--accent)" strokeWidth="1.8" strokeLinecap="round" />
    </svg>
  );
}
