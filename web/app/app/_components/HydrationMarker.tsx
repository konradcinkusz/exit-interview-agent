"use client";

import { usePathname } from "next/navigation";
import { useEffect } from "react";

/**
 * Marks the document with the path of the page once React has taken it over (`<html data-hydrated="/interview">`). Sign-in ends
 * with a full page load, and a server-rendered button does nothing until hydration; the browser suite waits for this marker
 * instead of guessing. It carries the path only, never content.
 */
export function HydrationMarker() {
  const pathname = usePathname();
  useEffect(() => {
    document.documentElement.dataset.hydrated = pathname;
  }, [pathname]);
  return null;
}
