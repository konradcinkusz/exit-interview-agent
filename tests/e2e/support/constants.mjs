// Shared by the stub backend and the specs. Fake values that exist only for browser tests.
export const ISSUER = "e2e-issuer";
export const AUDIENCE = "e2e-audience";
export const FAKE_EMAIL = "persona@example.invalid";
export const FAKE_PASSWORD = "dev-only-e2e-password";
export const STUB_URL = "http://localhost:4010";
export const TERMS_VERSION = "2026-01-01";
export const PRIVACY_VERSION = "2026-01-01";
export const emailFor = (name) => `${name}@example.invalid`;
