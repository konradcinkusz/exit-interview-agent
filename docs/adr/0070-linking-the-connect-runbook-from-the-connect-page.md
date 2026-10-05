# 0070. How the connect page points at the operator runbook

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: `frontend-bff` §2 (no environment-specific address baked in a build); P8; the project rule "do not invent a URL".

## Context

[`docs/guides/connect-claude.md`](../guides/connect-claude.md) (T8) is a runbook for whoever operates a deployment, with each step marked as documented, repository fact or not verified. The `/connect` page is for a user. There is no documentation
site and nothing is deployed, so a deployment has no docs location of its own.

## Decision

The page links to the file in the project repository on GitHub, `https://github.com/konradcinkusz/exit-interview-agent/blob/main/docs/guides/connect-claude.md`, and **also shows the repository path as text**. This is the same place the privacy
page already links its full documents (`m.privacy.links`), so there is one convention. The link says who it is for ("Running this deployment yourself?") and what it contains ("what was and was not verified"). It is a plain `<a>` with `rel="noreferrer noopener"`,
the only outbound link on the page, and the CSP is unaffected (a link is navigation, not a load).

## Consequences

- If the repository moves or stays private, the link breaks for readers without access; the path text still lets an operator find the file in their checkout.
- When a documentation site exists, the three `m.connect.runbook*` strings change and nothing else.
