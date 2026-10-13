// Platform drafts shaped exactly like the service's `tiles.drafts` (platform-drafts-spec §3.4), in both languages and at both risk
// levels, for the unit tests. The browser suite injects its own. Synthetic: no real company, person or site text.

import type { PlatformDraft } from "./interview-contract";

/** A high-risk Polish draft (GoWork: no labels in the copy, the warning names the risk). */
export const goWorkDraftPl: PlatformDraft = {
  platform: "gowork",
  name: "GoWork.pl",
  family: "structured_review",
  risk: "high",
  url: "https://www.gowork.pl/",
  warning: "To szkic, nie fakt. To nie jest porada prawna.",
  sections: [
    { key: "pros", label: "Plusy", text: "Dobry zespół i jasne zasady pracy. <b>Nie</b> & \"zawsze\" działało." },
    { key: "cons", label: "Minusy", text: "Rzadkie spotkania 1:1 z przełożonym." },
  ],
  copyText: "Dobry zespół i jasne zasady pracy. <b>Nie</b> & \"zawsze\" działało.\n\nRzadkie spotkania 1:1 z przełożonym.",
  fieldsOnSite: ["Status zatrudnienia", "Stanowisko"],
  primary: true,
};

/** A medium-risk English draft (Glassdoor: pseudonymous-verified identity, low detail). */
export const glassdoorDraftEn: PlatformDraft = {
  platform: "glassdoor",
  name: "Glassdoor",
  family: "structured_review",
  risk: "medium",
  url: "https://www.glassdoor.com/",
  warning: "Draft, not fact. Not legal advice.",
  sections: [
    { key: "title", label: "Headline", text: "Good onboarding, unclear growth" },
    { key: "pros", label: "Pros", text: "The onboarding plan was clear and the team helped in week one." },
    { key: "cons", label: "Cons", text: "Promotion criteria were never written down." },
  ],
  copyText: "Good onboarding, unclear growth\n\nThe onboarding plan was clear and the team helped in week one.\n\nPromotion criteria were never written down.",
  fieldsOnSite: ["Employment status and job title", "Star ratings"],
  primary: true,
};

/** A high-risk English draft (LinkedIn: real name, so the risk is high by the spec's rule). */
export const linkedinDraftEn: PlatformDraft = {
  platform: "linkedin",
  name: "LinkedIn",
  family: "professional_post",
  risk: "high",
  url: "https://www.linkedin.com/",
  warning: "Draft, not fact. Not legal advice. Your name is on this post.",
  sections: [{ key: "body", label: "", text: "My last year there taught me what I want from a manager." }],
  copyText: "My last year there taught me what I want from a manager.",
  fieldsOnSite: ["The post text, written by you"],
  primary: true,
};

/** A medium-risk Polish short post (X: the limit is 280 characters on the platform). */
export const xDraftPl: PlatformDraft = {
  platform: "x",
  name: "X",
  family: "short_post",
  risk: "medium",
  url: "https://x.com/",
  warning: "To szkic, nie fakt. To nie jest porada prawna.",
  sections: [{ key: "body", label: "", text: "Pierwsze tygodnie były dobre, potem zabrakło kierunku." }],
  copyText: "Pierwsze tygodnie były dobre, potem zabrakło kierunku.",
  fieldsOnSite: [],
  primary: true,
};

/** A high-risk English draft that is not primary: GoWork's market is Polish, so after an English interview it sits under "Other platforms". */
export const goWorkDraftEn: PlatformDraft = {
  platform: "gowork",
  name: "GoWork.pl",
  family: "structured_review",
  risk: "high",
  url: "https://www.gowork.pl/",
  warning: "Draft, not fact. Not legal advice.",
  sections: [{ key: "pros", label: "Pros", text: "Good team and clear working rules." }],
  copyText: "Good team and clear working rules.",
  fieldsOnSite: ["Employment status"],
  primary: false,
};

export const draftsPl: PlatformDraft[] = [goWorkDraftPl, xDraftPl];
export const draftsEn: PlatformDraft[] = [glassdoorDraftEn, linkedinDraftEn];
