// Runtime configuration (FRONTEND-BFF §2, P12): everything environment-specific is read from the
// environment at request time, never baked at build time, so one image serves every environment.
// No NEXT_PUBLIC_* variable is used anywhere, and no backend address ever reaches the browser.

export type Env = Record<string, string | undefined>;

export interface AuthConfig {
  /** Public base URL of this system's authservice instance; undefined => identity not configured (P8). */
  url?: string;
  issuer?: string;
  audience?: string;
  /** Cookies are `secure` unless explicitly disabled for plain-http local development. */
  secureCookies: boolean;
}

const trimSlash = (value?: string) => value?.trim().replace(/\/+$/, "") || undefined;

export function authConfig(env: Env = process.env): AuthConfig {
  return {
    url: trimSlash(env.AUTH_SERVICE_URL),
    issuer: env.AUTH_ISSUER || undefined,
    audience: env.AUTH_AUDIENCE || undefined,
    secureCookies: env.SESSION_COOKIE_SECURE !== "false",
  };
}

/**
 * The public https URL of the MCP endpoint (interview-service `Mcp:ResourceUrl`), or undefined when the Claude connector is
 * not set up in this environment (P8). Shown on the "Connect your AI client" page; not a secret.
 */
export function mcpResourceUrl(env: Env = process.env): string | undefined {
  const url = trimSlash(env.MCP_RESOURCE_URL);
  return url && /^https:\/\//.test(url) ? url : undefined;
}

export const identityConfigured = (cfg: AuthConfig) => Boolean(cfg.url && cfg.issuer && cfg.audience);

/** The one backend today. A second service adds a row here and a prefix in `routeFor` (lib/proxy-routing.ts). */
export type BackendName = "interview-service";

interface BackendSpec {
  /** Explicit address, set by the platform config (the public URL on Fly). First rung of the ladder. */
  explicitVar: string;
  /**
   * Service name as the AppHost publishes it: `services__<name>__<scheme>__0` for .NET consumers and
   * `<NAME>_<SCHEME>` (upper snake case) for JavaScript apps.
   */
  discoveryName: string;
  /** Internal DNS name derived from the Fly app name, e.g. `<slug>-web-<env>` -> `<slug>-interview-service-<env>`. */
  flyInternal: (flyApp: string) => string | undefined;
  localhost: string;
}

const BACKENDS: Record<BackendName, BackendSpec> = {
  "interview-service": {
    explicitVar: "INTERVIEW_SERVICE_URL",
    discoveryName: "interview-service",
    flyInternal: (flyApp) => {
      const m = /^(.*)-web-(.*)$/.exec(flyApp);
      return m ? `http://${m[1]}-interview-service-${m[2]}.internal:8080` : undefined;
    },
    localhost: "http://localhost:5200",
  },
};

/**
 * The candidate ladder (FRONTEND-BFF §5): explicit env var -> orchestrator service-discovery variables
 * -> internal DNS -> localhost. Callers try the candidates in order. One code path serves a laptop,
 * the AppHost and Fly, with no per-environment branches.
 */
export function backendCandidates(name: BackendName, env: Env = process.env): string[] {
  const spec = BACKENDS[name];
  const candidates = [
    trimSlash(env[spec.explicitVar]),
    trimSlash(env[`services__${spec.discoveryName}__https__0`]),
    trimSlash(env[`services__${spec.discoveryName}__http__0`]),
    trimSlash(env[`${spec.discoveryName.toUpperCase().replace(/-/g, "_")}_HTTPS`]),
    trimSlash(env[`${spec.discoveryName.toUpperCase().replace(/-/g, "_")}_HTTP`]),
    env.FLY_APP_NAME ? spec.flyInternal(env.FLY_APP_NAME) : undefined,
    spec.localhost,
  ];
  return [...new Set(candidates.filter((c): c is string => Boolean(c)))];
}
