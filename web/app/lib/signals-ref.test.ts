import { describe, expect, it } from "vitest";
import { employerPath, isEmployerRef } from "./signals-ref";

describe("isEmployerRef (the API's pattern, checked before any request)", () => {
  it.each(["abc", "demo-acme-co", "a1b2", "x".repeat(64), "demo-1-2-3"])("accepts %s", (v) => expect(isEmployerRef(v)).toBe(true));

  it.each([
    "", "ab", "x".repeat(65), "Abc", "a b c", "-abc", "abc-", "a--b", "a_b", "abc/def", "../x", "abc%2f", "abc\n", "abéc", "a.b",
    "<script>", "abc?x=1", "abc#frag",
  ])("rejects %j", (v) => expect(isEmployerRef(v)).toBe(false));

  it.each([undefined, null, 5, {}, []])("rejects a non-string %j", (v) => expect(isEmployerRef(v)).toBe(false));
});

describe("employerPath", () => {
  it("builds the path only for a valid reference", () => {
    expect(employerPath("demo-acme")).toBe("/signals/employers/demo-acme");
    expect(employerPath("../me")).toBeNull();
    expect(employerPath("a/b")).toBeNull();
  });
});
