# 0007. The record's context is three coarse bands, never free text

- Status: accepted
- Date: 2026-10-05
- Principle or guide served (or deviated from): brief §6 (privacy design), `metric-ethics` §1 (anti-goals enforced by architecture), P14 (documented decisions)

## Context

Aggregated employer signals are more useful when they can be cut by tenure, seniority or function. Every
extra attribute on a record also shrinks the group of people it could describe. With one employer, a record
carrying "tenure 4 years, head of the Gdansk office, finance" describes one person. The brief requires coarse
bands only and no free-text context.

## Decision

`context` holds exactly three fields, each an enum, so a client cannot put prose in them:

| Field | Values | Required |
|---|---|---|
| `tenureBand` | `lt_6m`, `6m_1y`, `1y_3y`, `3y_5y`, `5y_10y`, `gt_10y` | yes |
| `seniorityBand` | `junior`, `mid`, `senior`, `management` | no |
| `functionBand` | `engineering`, `product_design`, `sales_marketing`, `operations_support`, `corporate_functions`, `other` | no |

Reasoning for the shape:

- **Few, wide bands.** The full cross-product is 6 x 4 x 6 = 144 cells. A band set is chosen so that no band is
  meaningful on its own as "a handful of people" at a mid-sized employer: `management` merges team leads, managers
  and executives (a single-person "executive" band would be a name); `corporate_functions` merges finance, legal,
  HR and administration; `other` exists so that nobody has to pick a wrong band or invent a new one.
- **Tenure is the only required band** because it is the least identifying of the three and the most useful for
  interpreting the topics (a first-year view of onboarding differs from a ten-year one). The 6-month edge is the
  usual probation boundary; the open-ended top band avoids a "founding employee" singleton.
- **Optional bands are omitted, never null.** A person who judges that their seniority or function is identifying
  can leave it out; the schema forbids `null` so "withheld" and "absent" are one state.
- **Deliberately absent:** exact dates (join, leave, interview), age, gender or any protected category, location,
  employer size, manager or team identifiers, job title, salary. No timestamp of any kind is in the record:
  a timestamp is a join key to access logs.
- **Interview metadata is banded too:** duration (`lt_10m`..`gt_40m`) and turn count (`lt_10`..`gt_40`) are bands,
  because an exact turn count or duration is a fingerprint of the session and nothing downstream needs more.

What this does **not** do: bands do not make a record anonymous. The record stays pseudonymous. Two things still
carry re-identification risk and are handled elsewhere: the verbatim quotes (free text by nature; PII masking,
length caps, ADR-0010) and the combination of `employerRef` with the bands in a small employer.

## Consequences

- The signals module (T10) must apply its minimum-count threshold K to **every published cut**, including any
  band-by-band breakdown, not only to the employer total, and should publish single-band cuts only. That
  requirement is recorded here because the schema cannot enforce it.
- Adding a value to a band enum is an additive change (ADR-0009); splitting a band is not, because it would
  re-label existing records, and needs a new schema major version.
- Trigger to revisit: a measured case where a band combination yields groups below K for most employers.
