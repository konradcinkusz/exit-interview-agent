/**
 * The start page's illustration: a conversation turning into a short record. Pure inline SVG drawn with the theme's tokens, so it
 * follows light and dark; decorative (the text beside it says everything), and it loads nothing.
 */
export function HeroArt() {
  return (
    <svg className="hero-art" viewBox="0 0 360 300" aria-hidden="true" focusable="false">
      <defs>
        <linearGradient id="hero-glow" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stopColor="var(--accent)" stopOpacity="0.22" />
          <stop offset="1" stopColor="var(--accent)" stopOpacity="0" />
        </linearGradient>
      </defs>
      <circle cx="210" cy="140" r="130" fill="url(#hero-glow)" />
      {/* the interviewer's question */}
      <g>
        <rect x="24" y="34" width="210" height="62" rx="16" fill="var(--surface)" stroke="var(--border)" />
        <circle cx="50" cy="65" r="12" fill="var(--accent-soft)" />
        <path d="M45 65h10 M50 60v10" stroke="var(--accent)" strokeWidth="2.2" strokeLinecap="round" />
        <rect x="72" y="54" width="132" height="8" rx="4" fill="var(--fg)" opacity="0.75" />
        <rect x="72" y="70" width="96" height="8" rx="4" fill="var(--fg)" opacity="0.35" />
      </g>
      {/* the person's answer */}
      <g>
        <rect x="118" y="110" width="200" height="54" rx="16" fill="var(--accent)" />
        <rect x="136" y="127" width="150" height="8" rx="4" fill="var(--on-accent)" opacity="0.9" />
        <rect x="136" y="142" width="104" height="8" rx="4" fill="var(--on-accent)" opacity="0.55" />
      </g>
      {/* the record */}
      <g>
        <rect x="56" y="182" width="264" height="96" rx="18" fill="var(--surface)" stroke="var(--border)" />
        <rect x="78" y="202" width="70" height="8" rx="4" fill="var(--fg)" opacity="0.7" />
        {[0, 1, 2, 3, 4].map((i) => (
          <circle key={i} cx={84 + i * 20} cy={232} r="6.5" fill={i < 3 ? "var(--accent)" : "var(--surface-2)"} stroke={i < 3 ? "none" : "var(--border)"} />
        ))}
        <rect x="190" y="225" width="108" height="14" rx="7" fill="var(--accent-soft)" />
        <rect x="78" y="252" width="190" height="8" rx="4" fill="var(--fg)" opacity="0.3" />
      </g>
    </svg>
  );
}
