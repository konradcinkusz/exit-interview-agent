import path from "node:path";
import type { NextConfig } from "next";
import { securityHeaders } from "./lib/security-headers";

const config: NextConfig = {
  // Standalone output: the container runs `node server.js` with no node_modules install (P6).
  output: "standalone",
  // The workspace root, so standalone tracing includes workspace packages.
  outputFileTracingRoot: path.join(__dirname, ".."),
  poweredByHeader: false,
  headers: async () => [{ source: "/:path*", headers: securityHeaders(process.env.NODE_ENV === "production") }],
};

export default config;
