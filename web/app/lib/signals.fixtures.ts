// Response bodies shaped exactly like the Signals API (SignalsContracts.cs), for the unit tests. The browser suite has its own,
// served by the stub backend. Synthetic.

export const snapshot = {
  generatedAt: "2026-10-05T00:00:00+00:00",
  publicationIntervalHours: 24,
  rulesVersion: "1",
  minimumGroupSize: 5,
  deletionsAppearAtNextPublication: true,
};

export const stats = (n: number, mean: number, lower: number, upper: number, extra: Record<string, unknown> = {}) => ({
  n, mean, interval: { lower, upper, level: 0.95, method: "regularised-t" }, reliability: "moderate", coverage: "high", distribution: null, verification: null, ...extra,
});

const bands = (names: string[], shown: Record<string, ReturnType<typeof stats>>) =>
  names.map((band) => (shown[band] ? { band, status: "ok", stats: shown[band] } : { band, status: "none", stats: null }));

export const TENURE = ["lt_6m", "6m_1y", "1y_3y", "3y_5y", "5y_10y", "gt_10y"];

export const okTopic = (topic: string) => ({
  topic,
  status: "ok",
  overall: stats(12, 3.82, 3.4, 4.2, {
    distribution: [{ key: "low", count: 0 }, { key: "mid", count: 5 }, { key: "high", count: 7 }],
    verification: [{ key: "unchecked", count: 12 }, { key: "unverified", count: 0 }, { key: "verified", count: 0 }],
  }),
  cuts: [
    { dimension: "tenure", status: "published", cells: bands(TENURE, { "1y_3y": stats(6, 3.5, 2.9, 4.1), "3y_5y": stats(6, 4.1, 3.5, 4.6, { reliability: "low", coverage: "medium" }) }) },
    { dimension: "seniority", status: "suppressed", cells: [] },
    { dimension: "function", status: "suppressed", cells: [] },
  ],
});

export const insufficientTopic = (topic: string) => ({
  topic,
  status: "insufficient_data",
  overall: null,
  cuts: ["tenure", "seniority", "function"].map((dimension) => ({ dimension, status: "suppressed", cells: [] })),
});

// Loosely typed on purpose: the tests mutate these bodies to build hostile and malformed responses.
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export type Loose = Record<string, any>;
export type EmployerBody = { snapshot: Loose; employerRef: string; respondentsBand: string; topics: Loose[] };

export const employerBody = (): EmployerBody => ({
  snapshot,
  employerRef: "demo-acme",
  respondentsBand: "10-24",
  topics: [okTopic("onboarding"), insufficientTopic("management"), okTopic("growth"), insufficientTopic("pay_vs_promises"), okTopic("culture"), insufficientTopic("reason_for_leaving")],
});

export const listBody = (employers: string[], extra: Record<string, unknown> = {}) => ({ snapshot, employers, page: 1, limit: 20, total: employers.length, ...extra });
