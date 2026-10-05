import { describe, expect, it } from "vitest";
import { safeRedirect } from "./safe-redirect";

describe("safeRedirect", () => {
  it.each(["/account", "/account?x=1", "/a/b"])("keeps the same-origin path %s", (p) => expect(safeRedirect(p)).toBe(p));
  it.each(["https://evil.example", "//evil.example", "/\\evil.example", "javascript:alert(1)", "", null, undefined])(
    "replaces %s with the fallback",
    (p) => expect(safeRedirect(p)).toBe("/account"),
  );
});
