import path from "node:path";
import type { NextConfig } from "next";

const config: NextConfig = {
  // Standalone output: the container runs `node server.js` with no node_modules install (P6).
  output: "standalone",
  // The workspace root, so standalone tracing includes workspace packages.
  outputFileTracingRoot: path.join(__dirname, ".."),
  poweredByHeader: false,
};

export default config;
