import { describe, expect, it } from "vitest";
import { isSameOriginRequest, isStateChanging } from "./csrf";

const h = (init: Record<string, string>) => new Headers(init);

describe("isStateChanging", () => {
  it("treats only GET, HEAD and OPTIONS as safe", () => {
    expect(["GET", "HEAD", "OPTIONS"].some(isStateChanging)).toBe(false);
    expect(["POST", "PUT", "PATCH", "DELETE", "post"].every(isStateChanging)).toBe(true);
  });
});

describe("isSameOriginRequest", () => {
  it("accepts a browser request from this origin", () => {
    expect(isSameOriginRequest(h({ origin: "https://app.example.test", host: "app.example.test", "sec-fetch-site": "same-origin" }))).toBe(true);
  });

  it("refuses a cross-site or same-site-different-origin browser request", () => {
    expect(isSameOriginRequest(h({ "sec-fetch-site": "cross-site", host: "app.example.test" }))).toBe(false);
    expect(isSameOriginRequest(h({ "sec-fetch-site": "same-site", host: "app.example.test" }))).toBe(false);
  });

  it("refuses an Origin that is not this host, including the opaque origin", () => {
    expect(isSameOriginRequest(h({ origin: "https://evil.example.test", host: "app.example.test" }))).toBe(false);
    expect(isSameOriginRequest(h({ origin: "null", host: "app.example.test" }))).toBe(false);
    expect(isSameOriginRequest(h({ origin: "https://app.example.test:8443", host: "app.example.test" }))).toBe(false);
  });

  it("honours the forwarded host behind a platform proxy", () => {
    expect(isSameOriginRequest(h({ origin: "https://public.example.test", host: "internal:8080", "x-forwarded-host": "public.example.test" }))).toBe(true);
  });

  it("lets a request with no browser headers through: it cannot borrow a browser's cookies", () => {
    expect(isSameOriginRequest(h({ host: "app.example.test" }))).toBe(true);
  });
});
