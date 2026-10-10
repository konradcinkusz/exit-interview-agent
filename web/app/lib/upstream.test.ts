import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { callBackend, signalsCacheControl } from "./upstream";

// The BFF's passthrough of what makes the Signals reads revalidatable (ETag, Last-Modified, a bounded private lifetime,
// Retry-After) and nothing that would make another answer cacheable (ADR-0068).

const fetchMock = vi.fn();
beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
  process.env.INTERVIEW_SERVICE_URL = "http://interview.invalid";
});
afterEach(() => vi.unstubAllGlobals());

const upstream = (status: number, headers: Record<string, string>, body: string | null = "{}") =>
  fetchMock.mockResolvedValue(new Response(body, { status, headers }));
const call = (signalsRead: boolean, extra: Record<string, string> = {}) =>
  callBackend({ backend: "interview-service", upstreamPath: "/api/v1/signals/employers", method: "GET", headers: new Headers(extra), signalsRead });
const answer = async (signalsRead: boolean) => {
  const r = await call(signalsRead);
  if (r === "backend_unavailable") throw new Error("unavailable");
  return r;
};

describe("signalsCacheControl", () => {
  it.each([
    ["private, max-age=3600", "private, max-age=3600"],
    ["private, max-age=60", "private, max-age=60"],
    ["private, max-age=99999999", "private, max-age=604800"],
  ])("%s -> %s", (input, output) => expect(signalsCacheControl(input)).toBe(output));

  it.each([null, "", "public, max-age=3600", "max-age=3600", "private", "private, max-age=-1", "private, max-age=3600, public", "no-store", "private, max-age=1e9"])(
    "%j is never passed on: no-store",
    (input) => expect(signalsCacheControl(input)).toBe("no-store"),
  );
});

describe("a signals read", () => {
  it("passes the ETag, Last-Modified and a private lifetime, and varies on the session cookie", async () => {
    upstream(200, {
      "content-type": "application/json", etag: 'W/"s3-employers"', "last-modified": "Mon, 05 Oct 2026 00:00:00 GMT",
      "cache-control": "private, max-age=3600", vary: "Authorization",
    });

    const r = await answer(true);

    expect(r.status).toBe(200);
    expect(r.headers.get("etag")).toBe('W/"s3-employers"');
    expect(r.headers.get("last-modified")).toBe("Mon, 05 Oct 2026 00:00:00 GMT");
    expect(r.headers.get("cache-control")).toBe("private, max-age=3600");
    expect(r.headers.get("vary")).toBe("Cookie");
  });

  it("never lets an answer become public, whatever the service said", async () => {
    upstream(200, { etag: 'W/"x"', "cache-control": "public, max-age=3600" });
    expect((await answer(true)).headers.get("cache-control")).toBe("no-store");
  });

  it("answers 304 with no body and keeps the validators and lifetime (it renews the browser's copy)", async () => {
    upstream(304, { etag: 'W/"s3-employers"', "cache-control": "private, max-age=1200" }, null);

    const r = await answer(true);

    expect(r.status).toBe(304);
    expect(r.body).toBeNull();
    expect(r.headers.get("etag")).toBe('W/"s3-employers"');
    expect(r.headers.get("cache-control")).toBe("private, max-age=1200");
  });

  it("lets the uniform 404 be kept for the batch, like the service says", async () => {
    upstream(404, { "content-type": "application/problem+json", "cache-control": "private, max-age=600" });
    expect((await answer(true)).headers.get("cache-control")).toBe("private, max-age=600");
  });

  it("passes Retry-After on a 429 and keeps it no-store, with no validators", async () => {
    upstream(429, { "content-type": "application/json", "retry-after": "17", etag: 'W/"nope"', "cache-control": "private, max-age=600" });

    const r = await answer(true);

    expect(r.status).toBe(429);
    expect(r.headers.get("retry-after")).toBe("17");
    expect(r.headers.get("cache-control")).toBe("no-store");
    expect(r.headers.get("etag")).toBeNull();
  });

  it.each([400, 401, 500, 503])("never caches a %i", async (status) => {
    upstream(status, { "cache-control": "private, max-age=600", etag: 'W/"x"' });
    const r = await answer(true);
    expect(r.headers.get("cache-control")).toBe("no-store");
    expect(r.headers.get("etag")).toBeNull();
  });
});

describe("any other call", () => {
  it("stays no-store and carries no validators, even if the service sent them", async () => {
    upstream(200, { etag: 'W/"x"', "last-modified": "Mon, 05 Oct 2026 00:00:00 GMT", "cache-control": "private, max-age=3600" });

    const r = await answer(false);

    expect(r.headers.get("cache-control")).toBe("no-store");
    expect(r.headers.get("etag")).toBeNull();
    expect(r.headers.get("last-modified")).toBeNull();
    expect(r.headers.get("vary")).toBeNull();
  });
});

describe("the request side", () => {
  it("sends exactly the headers it was given (If-None-Match only when the route chose to forward it)", async () => {
    upstream(304, {}, null);
    await call(true, { "if-none-match": 'W/"s3-employers"', authorization: "Bearer t" });

    const sent = fetchMock.mock.calls[0]![1].headers as Headers;
    expect(sent.get("if-none-match")).toBe('W/"s3-employers"');
    expect(sent.get("cookie")).toBeNull();
  });
});

describe("redirects between services", () => {
  it("are still a bad gateway (a 3xx other than 304 is always a configuration bug)", async () => {
    upstream(302, { location: "http://elsewhere.invalid/" }, null);
    expect((await answer(true)).status).toBe(502);
  });
});

describe("a 403 from a candidate (the ladder, FRONTEND-BFF §5)", () => {
  const problem = (code: string) =>
    new Response(JSON.stringify({ type: `urn:exit-interview-agent:problem:${code}`, title: code, status: 403, code }), {
      status: 403,
      headers: { "content-type": "application/problem+json" },
    });

  it("passes a problem document with a code to the client, and does not try the next candidate", async () => {
    fetchMock.mockResolvedValueOnce(problem("email_not_verified"));

    const r = await call(false);

    expect(r).not.toBe("backend_unavailable");
    if (r === "backend_unavailable") return;
    expect(r.status).toBe(403);
    expect(await r.json()).toMatchObject({ code: "email_not_verified" });
    expect(r.headers.get("cache-control")).toBe("no-store");
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("treats a 403 with no problem code as the wrong ingress and moves to the next candidate", async () => {
    fetchMock.mockResolvedValueOnce(new Response("<html>forbidden</html>", { status: 403, headers: { "content-type": "text/html" } }));
    fetchMock.mockResolvedValueOnce(new Response("{}", { status: 200, headers: { "content-type": "application/json" } }));

    const r = await call(false);

    expect(r).not.toBe("backend_unavailable");
    if (r === "backend_unavailable") return;
    expect(r.status).toBe(200);
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it("does not take a JSON body without the problem media type as a service answer", async () => {
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify({ code: "email_not_verified" }), { status: 403, headers: { "content-type": "application/json" } }));
    fetchMock.mockResolvedValueOnce(new Response("{}", { status: 200, headers: { "content-type": "application/json" } }));

    const r = await call(false);

    expect(r).not.toBe("backend_unavailable");
    if (r === "backend_unavailable") return;
    expect(r.status).toBe(200);
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it("does not take a problem document without a string code as a service answer", async () => {
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify({ title: "forbidden" }), { status: 403, headers: { "content-type": "application/problem+json" } }));
    fetchMock.mockResolvedValueOnce(new Response("{}", { status: 200, headers: { "content-type": "application/json" } }));

    const r = await call(false);

    expect(r).not.toBe("backend_unavailable");
    if (r === "backend_unavailable") return;
    expect(r.status).toBe(200);
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it("answers 503 when every candidate refuses with no problem code", async () => {
    fetchMock.mockResolvedValue(new Response("nope", { status: 403, headers: { "content-type": "text/plain" } }));
    expect(await call(false)).toBe("backend_unavailable");
  });
});
