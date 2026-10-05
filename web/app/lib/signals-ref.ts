// The employer reference as the API defines it (src/ExitInterviewAgent.Records `EmployerRef`): lower-case ASCII words
// joined by single hyphens, 3 to 64 characters. It is checked here BEFORE any request is made, so a string that is not
// a reference never reaches the network (and so no API-provided or user-typed string is ever built into a URL unchecked).

const PATTERN = /^[a-z0-9]+(-[a-z0-9]+)*$/;

export function isEmployerRef(value: unknown): value is string {
  return typeof value === "string" && value.length >= 3 && value.length <= 64 && PATTERN.test(value);
}

/** The only place a reference becomes part of a path: validated, then percent-encoded (a no-op for a valid one). */
export function employerPath(employerRef: string): string | null {
  return isEmployerRef(employerRef) ? `/signals/employers/${encodeURIComponent(employerRef)}` : null;
}
