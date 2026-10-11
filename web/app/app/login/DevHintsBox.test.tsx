import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { messagesFor } from "@/lib/messages";
import { devHints } from "@/lib/runtime-config";
import { DevHintsBox } from "./DevHintsBox";

// The sign-in page renders <DevHintsBox hints={devHints()} />. Without DEV_HINTS nothing at all reaches the page: no box, no
// address, no password.

describe("the local-development box on the sign-in page", () => {
  it("renders nothing without DEV_HINTS", () => {
    const html = renderToStaticMarkup(<DevHintsBox hints={devHints({})} t={messagesFor("en").devHints} />);
    expect(html).toBe("");
  });

  it("with DEV_HINTS=1 shows the test account, labelled as local development, with the seed command", () => {
    const html = renderToStaticMarkup(<DevHintsBox hints={devHints({ DEV_HINTS: "1" })} t={messagesFor("en").devHints} />);
    expect(html).toContain("Local development");
    expect(html).toContain("tester@example.com");
    expect(html).toContain("Dev-Tester-Pass-1");
    expect(html).toContain("Seed test user (with credits)");
  });

  it("is translated", () => {
    const html = renderToStaticMarkup(<DevHintsBox hints={devHints({ DEV_HINTS: "1" })} t={messagesFor("pl").devHints} />);
    expect(html).toContain("Lokalne środowisko deweloperskie");
  });
});
