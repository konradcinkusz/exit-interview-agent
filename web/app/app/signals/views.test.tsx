import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { employerBody, type EmployerBody, insufficientTopic, listBody, okTopic } from "@/lib/signals.fixtures";
import { readEmployer, readEmployerList, type Employer, type EmployerList } from "@/lib/signals";
import { EmployerListView, EmployerView, NothingToShow } from "./views";

const employer = (mutate: (b: EmployerBody) => void = () => {}): Employer => {
  const body = structuredClone(employerBody());
  mutate(body);
  const e = readEmployer(body);
  if (!e) throw new Error("fixture not readable");
  return e;
};
const list = (employers: string[], extra: Record<string, unknown> = {}): EmployerList => {
  const l = readEmployerList(listBody(employers, extra));
  if (!l) throw new Error("fixture not readable");
  return l;
};
const html = (node: React.ReactElement) => renderToStaticMarkup(node);
const text = (markup: string) => markup.replace(/<[^>]+>/g, " ").replace(/\s+/g, " ").trim();
const section = (markup: string, topic: string) => new RegExp(`<section[^>]*data-testid="topic-${topic}"[^>]*>(.*?)</section>`).exec(markup)![1]!;
const cut = (topicMarkup: string, dimension: string) => new RegExp(`<div data-testid="cut-${dimension}"[^>]*>(.*?)</div>`).exec(topicMarkup)![1]!;

describe("the employer view shows exactly what the API returned", () => {
  const markup = html(<EmployerView employer={employer()} />);

  it("puts the mean, its interval and n on the same line, as one text node", () => {
    expect(markup).toContain('<span class="stat-line" data-testid="stat-line">3.82 (95% interval 3.40-4.20), 12 ratings</span>');
  });

  it("shows reliability, coverage in the contract wording, the distribution and the verification breakdown with its label", () => {
    const t = text(section(markup, "onboarding"));
    expect(t).toContain("moderate: based on a fair number of ratings.");
    expect(t).toContain("high: the share of respondents who rated this topic.");
    expect(t).toContain("Low (1-2) 0");
    expect(t).toContain("Middle (3) 5");
    expect(t).toContain("High (4-5) 7");
    expect(t).toContain("Employment is claimed, not verified.");
    expect(t).toContain("Not checked 12");
    expect(t).toContain("A wide interval means early, not wrong.");
  });

  it("says the snapshot date, the once-per-update rule and that a deletion is counted until the next update", () => {
    const t = text(markup);
    expect(t).toMatch(/Updated October 5, 2026.* UTC\. Figures change once per update, not when someone submits\. A record deleted after this date is still counted until the next update\./);
    expect(t).toContain("Aggregated from records submitted by users of this tool. Employment is claimed, not verified.");
    expect(t).toContain("10-24 A range, not an exact count.");
    expect(t).toContain("A figure is shown only when it rests on at least 5 ratings.");
  });

  it("renders the six topics in the order the API sent them", () => {
    const order = [...markup.matchAll(/data-testid="topic-([a-z_]+)"/g)].map((x) => x[1]);
    expect(order).toEqual(["onboarding", "management", "growth", "pay_vs_promises", "culture", "reason_for_leaving"]);
    const reversed = employer((b) => b.topics.reverse());
    expect([...html(<EmployerView employer={reversed} />).matchAll(/data-testid="topic-([a-z_]+)"/g)].map((x) => x[1])[0]).toBe("reason_for_leaving");
  });

  it("shows no number at all for a topic with insufficient data", () => {
    const t = text(section(markup, "management"));
    expect(t).toBe("Management Not enough responses to show this topic.");
    expect(t).not.toMatch(/\d/);
  });

  it("shows a suppressed cut as one sentence and names no band", () => {
    const suppressed = cut(section(markup, "onboarding"), "seniority");
    expect(text(suppressed)).toBe("Breakdown by seniority This breakdown is hidden to protect small groups.");
    expect(suppressed).toContain("This breakdown is hidden to protect small groups.");
    for (const band of ["Junior", "Mid-level", "Senior", "Management"]) expect(suppressed).not.toContain(band);
    expect(suppressed).not.toContain("<table");
  });

  it("shows a published cut as a real table with headers, one line per band, and 'No ratings.' for an empty band", () => {
    const tenure = cut(section(markup, "onboarding"), "tenure");
    expect(tenure).toContain("<table");
    expect(tenure).toContain("<caption>Breakdown by tenure</caption>");
    expect(tenure).toContain('<th scope="col">Group</th>');
    expect(tenure).toContain('<th scope="row">1 to 3 years</th>');
    expect(text(tenure)).toContain("6 to 12 months No ratings.");
    expect(text(tenure)).toContain("3.50 (95% interval 2.90-4.10), 6 ratings");
    expect(text(tenure)).toContain("low: based on few ratings, so the figure can move a lot.");
    expect(text(tenure)).toContain("medium: the share of respondents who rated this topic.");
  });

  it("draws the interval as a decorative range bar that encodes the interval and the mean, not n", () => {
    const bar = (n: number) => {
      const e = employer((b) => {
        const o = (b.topics[0] as { overall: { n: number } }).overall;
        o.n = n;
      });
      return html(<EmployerView employer={e} />).match(/<svg class="range-bar".*?<\/svg>/)![0];
    };
    expect(bar(12)).toContain('aria-hidden="true"');
    expect(bar(12)).toBe(bar(5000));
  });
});

describe("what must never be on the page", () => {
  const markup = html(<EmployerView employer={employer()} />) + html(<EmployerListView list={list(["alpha", "beta"])} />);

  it("has no control that could sort, rank, filter, search or compare", () => {
    expect(markup).not.toMatch(/<(select|input|button|textarea|option)\b/);
    expect(markup).not.toMatch(/role="(search|combobox|listbox|slider|switch)"/);
    expect(markup).not.toMatch(/aria-sort|sortable|<form/);
  });

  it("has no ranking, score or comparison vocabulary in what it renders", () => {
    const t = text(html(<EmployerView employer={employer()} />)).toLowerCase();
    for (const word of ["best", "worst", "rank", "score", "average", "percentile", "trend", "top ", "compare", "versus other", "better", "worse"]) {
      expect(t, word).not.toContain(word);
    }
    expect(html(<EmployerView employer={employer()} />)).not.toMatch(/[↑↓▲▼⬆⬇]/);
  });

  it("uses no inline style, no dangerouslySetInnerHTML-shaped output and no colour-only meaning", () => {
    expect(markup).not.toMatch(/\sstyle=/);
    expect(markup).not.toMatch(/class="[^"]*(good|bad|green|red|amber|rank)/);
  });
});

describe("API strings are only ever text", () => {
  const hostile = '<img src=x onerror=alert(1)>"&';

  it("escapes a band, a topic and a group key from the API", () => {
    const e = employer((b) => {
      (b.topics[0] as { topic: string }).topic = hostile;
      const o = (b.topics[0] as { overall: { distribution: { key: string; count: number }[] } }).overall;
      o.distribution[0]!.key = hostile;
      ((b.topics[0] as { cuts: { cells: { band: string }[] }[] }).cuts[0]!.cells[0]!).band = hostile;
    });
    const markup = html(<EmployerView employer={e} />);
    expect(markup).not.toContain("<img");
    expect(markup).toContain("&lt;img src=x onerror=alert(1)&gt;");
  });

  it("links an employer only when the string is a valid reference, and encodes it", () => {
    const markup = html(<EmployerListView list={list(["demo-acme", "Bad Ref/../x", "<b>x</b>"])} />);
    expect(markup).toContain('href="/signals/demo-acme"');
    expect(markup).not.toContain('href="/signals/Bad');
    expect(markup).not.toContain("<b>");
    expect(markup.match(/<a /g)?.length).toBe(1);
  });
});

describe("the list", () => {
  it("shows the alphabetical sentence and renders the employers in the order given, never re-ordered", () => {
    const markup = html(<EmployerListView list={list(["zeta", "alpha", "mid"])} />);
    expect(text(markup)).toContain("Employers are listed alphabetically. This tool does not rank employers.");
    expect([...markup.matchAll(/href="\/signals\/([a-z]+)"/g)].map((x) => x[1])).toEqual(["zeta", "alpha", "mid"]);
  });

  it("shows no count of employers or respondents", () => {
    const t = text(html(<EmployerListView list={list(["a-b", "c-d", "e-f"])} />));
    expect(t).not.toMatch(/\b3\b/);
    expect(t).not.toMatch(/\d+ employers/i);
  });

  it("shows the empty state, and no list, when there is no snapshot or nothing to list", () => {
    for (const l of [readEmployerList({ snapshot: null, employers: [], page: 1, limit: 20, total: 0 })!, list([], { total: 0 })]) {
      const markup = html(<EmployerListView list={l} />);
      expect(markup).toContain('data-testid="signals-empty"');
      expect(markup).not.toContain("employer-list");
    }
  });

  it("pages with links only when the API's total says there is more", () => {
    expect(html(<EmployerListView list={list(["abc"], { total: 1 })} />)).not.toContain('data-testid="paging"');
    const middle = html(<EmployerListView list={list(["abc"], { page: 2, limit: 1, total: 3 })} />);
    expect(middle).toContain('href="/signals"');
    expect(middle).toContain('href="/signals?page=3"');
    const last = html(<EmployerListView list={list(["abc"], { page: 3, limit: 1, total: 3 })} />);
    expect(last).toContain('href="/signals?page=2"');
    expect(last).not.toContain("Next page");
    const beyond = html(<EmployerListView list={list([], { page: 99, limit: 1, total: 3 })} />);
    expect(beyond).toContain('href="/signals?page=3"');
  });
});

describe("nothing to show", () => {
  it("says the same thing whatever the reason, and does not say 'unknown'", () => {
    const t = text(html(<NothingToShow />));
    expect(t).toContain("There is nothing to show for this employer.");
    expect(t.toLowerCase()).not.toContain("unknown employer");
    expect(t.toLowerCase()).not.toContain("not found");
  });
});

describe("a topic that has data next to one that does not", () => {
  it("renders each on its own and never relates them", () => {
    const e = employer((b) => {
      b.topics = [okTopic("culture"), insufficientTopic("growth")];
    });
    const markup = html(<EmployerView employer={e} />);
    expect(text(section(markup, "growth"))).not.toMatch(/\d/);
    expect(markup).toContain("3.82");
  });
});
