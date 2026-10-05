import { defineConfig } from "@playwright/test";

const STUB_PORT = 4010;
const WEB_PORT = 4011;
const ci = Boolean(process.env.CI);

// Runs the PRODUCTION artifact (Next standalone output) against the stub backend. `pnpm build` in
// web/ must have run first; CI does that in the same job. Locally, PLAYWRIGHT_CHROMIUM_EXECUTABLE
// points at a preinstalled Chromium.
export default defineConfig({
  testDir: "./specs",
  fullyParallel: true,
  forbidOnly: ci,
  retries: ci ? 2 : 0,
  workers: ci ? 1 : undefined,
  reporter: ci ? [["list"], ["html", { open: "never" }]] : "list",
  use: {
    baseURL: `http://localhost:${WEB_PORT}`,
    trace: "on-first-retry",
    screenshot: "only-on-failure",
    launchOptions: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE
      ? { executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE }
      : {},
  },
  projects: [{ name: "chromium", use: { browserName: "chromium" } }],
  webServer: [
    {
      command: "node support/stub-backend.mjs",
      env: { STUB_PORT: String(STUB_PORT) },
      url: `http://localhost:${STUB_PORT}/health`,
      reuseExistingServer: !ci,
    },
    {
      command:
        "cp -r .next/static .next/standalone/app/.next/ && node .next/standalone/app/server.js",
      cwd: "../../web/app",
      env: {
        PORT: String(WEB_PORT),
        HOSTNAME: "localhost",
        AUTH_SERVICE_URL: `http://localhost:${STUB_PORT}`,
        AUTH_ISSUER: "e2e-issuer",
        AUTH_AUDIENCE: "e2e-audience",
        INTERVIEW_SERVICE_URL: `http://localhost:${STUB_PORT}`,
        SESSION_COOKIE_SECURE: "false",
      },
      url: `http://localhost:${WEB_PORT}/healthz`,
      reuseExistingServer: !ci,
    },
  ],
});
