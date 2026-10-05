import { describe, expect, it } from "vitest";
import { DELETION_FACTS } from "./copy";

describe("account deletion copy", () => {
  it("states the three facts users act on: login only, submissions untouched, receipt code is the way out", () => {
    const all = Object.values(DELETION_FACTS).join(" ");

    expect(DELETION_FACTS.removes).toMatch(/removes your login/i);
    expect(DELETION_FACTS.notSubmissions).toMatch(/does not delete anything you submitted/i);
    expect(DELETION_FACTS.notSubmissions).toMatch(/not linked to your account/i);
    expect(DELETION_FACTS.receipt).toMatch(/receipt code/i);
    expect(all).not.toMatch(/all your data (is|will be) (deleted|erased|removed)/i); // the claim the system cannot keep
  });
});
