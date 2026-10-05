import { describe, expect, it } from "vitest";
import { routeFor } from "./proxy-routing";

describe("routeFor", () => {
  it("maps /api/proxy/v1/... to the interview-service's /api/v1/...", () => {
    expect(routeFor(["v1", "me"])).toEqual({ backend: "interview-service", upstreamPath: "/api/v1/me" });
  });

  it.each([[[]], [["v1"]], [["health"]], [["v1", ".."]], [["v1", "%2e%2e", "x"]], [["v1", "a\\b"]], [["openapi", "v1.json"]]])(
    "does not expose %j",
    (segments) => expect(routeFor(segments as string[])).toBeNull(),
  );
});
