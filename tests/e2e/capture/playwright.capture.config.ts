import { resolve } from "node:path";
import { defineConfig } from "@playwright/test";
import base from "../playwright.config";

// Screenshot capture for the UX write-ups under docs/ux/ (not part of the test suite). It runs the same production build
// against the same stub as the suite, with DEV_HINTS=1 so the "Local development" box on the sign-in page can be captured.
//   [CAPTURE_REUSE=1: use servers already running on the two ports] CAPTURE_DIR=<dir> PLAYWRIGHT_CHROMIUM_EXECUTABLE=/opt/pw-browsers/chromium npx playwright test -c capture/playwright.capture.config.ts [-g <state>]
const servers = Array.isArray(base.webServer) ? base.webServer : [];
export default defineConfig({
  ...base,
  testDir: ".",
  testMatch: "capture.spec.ts",
  fullyParallel: true,
  retries: 0,
  reporter: "list",
  // The base config's paths are relative to tests/e2e; this file lives one level down.
  webServer: servers.map((s) =>
    s.cwd
      ? { ...s, cwd: resolve(import.meta.dirname, "..", s.cwd), env: { ...s.env, DEV_HINTS: "1" }, reuseExistingServer: Boolean(process.env.CAPTURE_REUSE) }
      : { ...s, cwd: resolve(import.meta.dirname, "..") },
  ),
});
