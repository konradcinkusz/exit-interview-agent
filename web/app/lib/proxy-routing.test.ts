import { describe, expect, it } from "vitest";
import { hasInvalidEmployerRef, routeFor } from "./proxy-routing";

describe("routeFor", () => {
  it("maps /api/proxy/v1/... to the interview-service's /api/v1/...", () => {
    expect(routeFor(["v1", "me"])).toEqual({ backend: "interview-service", upstreamPath: "/api/v1/me", signalsRead: false });
  });

  it.each([[[]], [["v1"]], [["health"]], [["v1", ".."]], [["v1", "%2e%2e", "x"]], [["v1", "a\\b"]], [["openapi", "v1.json"]]])(
    "does not expose %j",
    (segments) => expect(routeFor(segments as string[])).toBeNull(),
  );

  it("marks exactly the two signals reads as cacheable", () => {
    expect(routeFor(["v1", "signals", "employers"])?.signalsRead).toBe(true);
    expect(routeFor(["v1", "signals", "employers", "demo-acme"])?.signalsRead).toBe(true);
    expect(routeFor(["v1", "signals", "employers", "demo-acme", "extra"])?.signalsRead).toBe(false);
    expect(routeFor(["v1", "signals", "other"])?.signalsRead).toBe(false);
    expect(routeFor(["v1", "tickets"])?.signalsRead).toBe(false);
    expect(routeFor(["v1", "signals"])?.signalsRead).toBe(false);
  });
});

describe("hasInvalidEmployerRef", () => {
  it("is true only for an employer path whose reference is not well formed", () => {
    expect(hasInvalidEmployerRef(["v1", "signals", "employers", "Not Valid"])).toBe(true);
    expect(hasInvalidEmployerRef(["v1", "signals", "employers", "ab"])).toBe(true);
    expect(hasInvalidEmployerRef(["v1", "signals", "employers", "demo-acme"])).toBe(false);
    expect(hasInvalidEmployerRef(["v1", "signals", "employers"])).toBe(false);
    expect(hasInvalidEmployerRef(["v1", "me"])).toBe(false);
  });
});
