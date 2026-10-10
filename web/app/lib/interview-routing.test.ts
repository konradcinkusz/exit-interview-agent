import { describe, expect, it } from "vitest";
import { isInterviewPath, routeFor } from "./proxy-routing";

// The interview endpoints are reachable through the catch-all proxy like every v1 path, but only these shapes are interview
// paths, and none of them is cacheable (the signals exception does not apply here).

describe("isInterviewPath", () => {
  it.each([
    [["v1", "interviews"]],
    [["v1", "interviews", "int_abcdefgh"]],
    [["v1", "interviews", "int_abcdefgh", "reply"]],
    [["v1", "interviews", "int_abcdefgh", "result"]],
    [["v1", "credits"]],
    [["v1", "checkout"]],
  ])("accepts %j", (segments) => expect(isInterviewPath(segments)).toBe(true));

  it.each([
    [["v1", "interviews", "int_abcdefgh", "delete"]],
    [["v1", "interviews", "int_abcdefgh", "reply", "extra"]],
    [["v1", "interviews", ""]],
    [["v1", "interviews", ".."]],
    [["v1", "interviews", "bad id with spaces"]],
    [["v1", "webhooks", "payments"]],
    [["v1", "tickets"]],
    [["interviews"]],
  ])("refuses %j", (segments) => expect(isInterviewPath(segments)).toBe(false));

  it("marks interview answers as never cacheable in the route", () => {
    expect(routeFor(["v1", "interviews", "int_abcdefgh", "result"])?.signalsRead).toBe(false);
  });
});
