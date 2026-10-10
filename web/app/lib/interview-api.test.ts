import { afterEach, describe, expect, it, vi } from "vitest";
import { failureFor, interviewApi, safeCheckoutUrl, type InterviewFailure } from "./interview-api";

// The client talks only to the BFF (`/api/proxy/v1/...`), never cached, and maps every non-2xx answer to a stable failure
// kind the page turns into copy. Nothing it handles (interview text, ids, tiles) may reach storage or a log (ADR-0048).

const started = {
  id: "int_abcdefgh",
  status: "in_progress",
  language: "en",
  expiresAt: "2026-10-10T12:00:00Z",
  turn: { index: 0, kind: "opening", text: "Hello, I am an AI interviewer." },
};

function json(status: number, body?: unknown, headers: Record<string, string> = {}) {
  return new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: body === undefined ? headers : { "content-type": "application/json", ...headers },
  });
}

const fetchReturning = (response: Response) => vi.fn(async () => response) as unknown as typeof fetch & ReturnType<typeof vi.fn>;

function callOf(mock: ReturnType<typeof vi.fn>, index = 0): [string, RequestInit] {
  return mock.mock.calls[index] as unknown as [string, RequestInit];
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("interviewApi calls", () => {
  it("starts an interview with a JSON body, through the BFF, never cached", async () => {
    const f = fetchReturning(json(201, started));
    const out = await interviewApi.start({ language: "en", tenure: "1y_3y" }, f);
    expect(out).toEqual({ ok: true, value: started });
    const [url, init] = callOf(f);
    expect(url).toBe("/api/proxy/v1/interviews");
    expect(init.method).toBe("POST");
    expect(init.cache).toBe("no-store");
    expect(new Headers(init.headers).get("content-type")).toBe("application/json");
    expect(JSON.parse(init.body as string)).toEqual({ language: "en", tenure: "1y_3y" });
  });

  it("sends one reply as { text } to the reply path and reads the next turn", async () => {
    const reply = { status: "in_progress", turn: { index: 1, kind: "probe", text: "Can you give an example?" } };
    const f = fetchReturning(json(200, reply));
    const out = await interviewApi.reply("int_abcdefgh", "I was not given a plan.", f);
    expect(out).toEqual({ ok: true, value: reply });
    const [url, init] = callOf(f);
    expect(url).toBe("/api/proxy/v1/interviews/int_abcdefgh/reply");
    expect(init.method).toBe("POST");
    expect(JSON.parse(init.body as string)).toEqual({ text: "I was not given a plan." });
  });

  it("reads state and result with GET, never cached", async () => {
    const state = { id: "int_abcdefgh", status: "in_progress", language: "en", turnCount: 2, expiresAt: "2026-10-10T12:00:00Z" };
    const f = fetchReturning(json(200, state));
    expect(await interviewApi.state("int_abcdefgh", f)).toEqual({ ok: true, value: state });
    expect(callOf(f)[0]).toBe("/api/proxy/v1/interviews/int_abcdefgh");
    expect(callOf(f)[1].cache).toBe("no-store");

    const g = fetchReturning(json(409, { code: "not_completed" }));
    expect(await interviewApi.result("int_abcdefgh", g)).toEqual({ ok: false, failure: { kind: "not_completed" } });
    expect(callOf(g)[0]).toBe("/api/proxy/v1/interviews/int_abcdefgh/result");
  });

  it("deletes with DELETE and treats 204 as done", async () => {
    const f = fetchReturning(new Response(null, { status: 204 }));
    expect(await interviewApi.remove("int_abcdefgh", f)).toEqual({ ok: true, value: undefined });
    expect(callOf(f)[1].method).toBe("DELETE");
  });

  it("reads the credit balance", async () => {
    const f = fetchReturning(json(200, { balance: 2 }));
    expect(await interviewApi.credits(f)).toEqual({ ok: true, value: { balance: 2 } });
    expect(callOf(f)[0]).toBe("/api/proxy/v1/credits");
  });

  it("asks for a checkout of a quantity and returns the provider page", async () => {
    const f = fetchReturning(json(200, { url: "https://checkout.example.invalid/session/abc" }));
    expect(await interviewApi.checkout(1, f)).toEqual({ ok: true, value: { url: "https://checkout.example.invalid/session/abc" } });
    expect(callOf(f)[0]).toBe("/api/proxy/v1/checkout");
    expect(JSON.parse(callOf(f)[1].body as string)).toEqual({ quantity: 1 });
  });

  it("refuses a checkout address that is not https", async () => {
    const f = fetchReturning(json(200, { url: "javascript:alert(1)" }));
    expect(await interviewApi.checkout(1, f)).toEqual({ ok: false, failure: { kind: "generic" } });
  });

  it("treats a success body of the wrong shape as a generic failure, not a crash", async () => {
    expect(await interviewApi.start({ language: "pl", tenure: "gt_10y" }, fetchReturning(json(201, {})))).toEqual({
      ok: false,
      failure: { kind: "generic" },
    });
  });

  it("turns a network error into 'unavailable'", async () => {
    const f = vi.fn(async () => {
      throw new TypeError("fetch failed");
    }) as unknown as typeof fetch;
    expect(await interviewApi.state("int_abcdefgh", f)).toEqual({ ok: false, failure: { kind: "unavailable" } });
  });
});

describe("failureFor: every answer the page must explain", () => {
  const cases: [string, number, unknown, string | null, InterviewFailure][] = [
    ["no credit", 402, { code: "payment_required" }, null, { kind: "payment_required" }],
    ["one open interview", 409, { code: "interview_in_progress" }, null, { kind: "interview_in_progress" }],
    ["already ended", 409, { code: "interview_ended" }, null, { kind: "interview_ended" }],
    ["result not ready", 409, { code: "not_completed" }, null, { kind: "not_completed" }],
    ["unknown 409", 409, { code: "other" }, null, { kind: "generic" }],
    ["expired or lost", 410, { code: "gone" }, null, { kind: "gone" }],
    ["unknown id", 404, { code: "not_found" }, null, { kind: "not_found" }],
    ["reply not accepted", 422, { code: "reply_invalid" }, null, { kind: "reply_invalid" }],
    ["rate limit, wait from body", 429, { error: "rate_limited", retryAfter: 30 }, null, { kind: "rate_limited", retryAfter: 30 }],
    ["rate limit, wait from header", 429, {}, "12", { kind: "rate_limited", retryAfter: 12 }],
    ["rate limit, no wait given", 429, {}, null, { kind: "rate_limited" }],
    ["kill switch or no provider", 503, { code: "interviews_disabled" }, null, { kind: "interviews_disabled" }],
    ["model down", 503, { code: "provider_unavailable" }, null, { kind: "provider_unavailable" }],
    ["billing off", 503, { code: "billing_disabled" }, null, { kind: "billing_disabled" }],
    ["backend unreachable", 503, { error: "backend_unavailable" }, null, { kind: "unavailable" }],
    ["gateway timeout", 504, { error: "gateway_timeout" }, null, { kind: "unavailable" }],
    ["signed out", 401, { error: "unauthenticated" }, null, { kind: "unauthenticated" }],
    ["terms not accepted", 403, { error: "consent_required" }, null, { kind: "consent_required" }],
    ["unknown status", 500, null, null, { kind: "generic" }],
  ];

  it.each(cases)("%s (%i)", (_label, status, body, header, expected) => {
    expect(failureFor(status, body, header)).toEqual(expected);
  });
});

describe("safeCheckoutUrl", () => {
  it("accepts only absolute https addresses", () => {
    expect(safeCheckoutUrl("https://checkout.example.invalid/s/1")).toBe("https://checkout.example.invalid/s/1");
    expect(safeCheckoutUrl("http://checkout.example.invalid/s/1")).toBeNull();
    expect(safeCheckoutUrl("/relative")).toBeNull();
    expect(safeCheckoutUrl("data:text/html,x")).toBeNull();
    expect(safeCheckoutUrl(42)).toBeNull();
  });
});

describe("the client keeps nothing and logs nothing", () => {
  it("writes no storage and no console output while handling interview text", async () => {
    const canary = "CANARY-interview-text-7f3a";
    const storageWrite = vi.fn();
    const storage = { setItem: storageWrite, removeItem: storageWrite, clear: storageWrite, getItem: vi.fn(), key: vi.fn(), length: 0 };
    vi.stubGlobal("localStorage", storage);
    vi.stubGlobal("sessionStorage", storage);
    const consoleSpies = (["log", "info", "warn", "error", "debug"] as const).map((k) => vi.spyOn(console, k).mockImplementation(() => {}));

    const f = vi
      .fn()
      .mockResolvedValueOnce(json(201, started))
      .mockResolvedValueOnce(json(200, { status: "in_progress", turn: { index: 1, kind: "topic", text: canary } }))
      .mockResolvedValueOnce(json(409, { code: "interview_ended" })) as unknown as typeof fetch;
    await interviewApi.start({ language: "en", tenure: "gt_10y" }, f);
    await interviewApi.reply("int_abcdefgh", canary, f);
    await interviewApi.reply("int_abcdefgh", canary, f);

    expect(storageWrite).not.toHaveBeenCalled();
    for (const spy of consoleSpies) expect(spy).not.toHaveBeenCalled();
    // The text travels in the body of a POST, never in a URL.
    const urls = (f as unknown as ReturnType<typeof vi.fn>).mock.calls.map((c) => String(c[0]));
    expect(urls.some((u) => u.includes(canary))).toBe(false);
  });
});
