import { useEffect, useState } from "react";

/**
 * Seconds left of a wait the service asked for (a 429's retryAfter), counting down to 0. A new failure (`key`) restarts it. The
 * count follows the clock (a deadline), not a chain of one-second timers, so it stays right when a background tab is throttled.
 * Nothing is retried automatically: the caller only re-enables its control at 0.
 */
export function useCountdown(seconds: number | undefined, key: unknown): number {
  const [tracked, setTracked] = useState({ key, left: seconds ?? 0 });
  // A new failure resets the count while rendering (React's "adjust state when a prop changes" pattern), not in an effect.
  if (tracked.key !== key) setTracked({ key, left: seconds ?? 0 });

  useEffect(() => {
    if (!seconds) return;
    const deadline = Date.now() + seconds * 1000;
    const timer = window.setInterval(() => {
      const left = Math.max(0, Math.ceil((deadline - Date.now()) / 1000));
      setTracked((t) => (t.key === key ? { key, left } : t));
      if (left === 0) window.clearInterval(timer);
    }, 250);
    return () => window.clearInterval(timer);
  }, [seconds, key]);

  return tracked.key === key ? tracked.left : (seconds ?? 0);
}
