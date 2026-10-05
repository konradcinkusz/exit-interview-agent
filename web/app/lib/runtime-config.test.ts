import { describe, expect, it } from "vitest";
import { authConfig, backendCandidates, identityConfigured } from "./runtime-config";

describe("backendCandidates (the candidate ladder)", () => {
  it("orders explicit variable, service discovery, internal DNS, then localhost", () => {
    const env = {
      INTERVIEW_SERVICE_URL: "https://public.example.invalid/",
      "services__interview-service__https__0": "https://discovered.example.invalid",
      "services__interview-service__http__0": "http://localhost:5201",
      FLY_APP_NAME: "exit-interview-agent-web-dev",
    };

    expect(backendCandidates("interview-service", env)).toEqual([
      "https://public.example.invalid",
      "https://discovered.example.invalid",
      "http://localhost:5201",
      "http://exit-interview-agent-interview-service-dev.internal:8080",
      "http://localhost:5200",
    ]);
  });

  it("falls back to localhost only when nothing is configured, with no duplicates", () => {
    expect(backendCandidates("interview-service", {})).toEqual(["http://localhost:5200"]);
  });
});

describe("authConfig", () => {
  it("is not configured without url, issuer and audience, and reads the environment per call", () => {
    expect(identityConfigured(authConfig({}))).toBe(false);
    const env = { AUTH_SERVICE_URL: "http://localhost:5100/", AUTH_ISSUER: "i", AUTH_AUDIENCE: "a" };
    const first = authConfig(env);
    env.AUTH_SERVICE_URL = "http://localhost:5101";
    expect(first.url).toBe("http://localhost:5100");
    expect(authConfig(env).url).toBe("http://localhost:5101");
  });

  it("marks cookies secure unless explicitly disabled", () => {
    expect(authConfig({}).secureCookies).toBe(true);
    expect(authConfig({ SESSION_COOKIE_SECURE: "false" }).secureCookies).toBe(false);
  });
});
