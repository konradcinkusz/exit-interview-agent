import { CONSENT_SUMMARY, DELETION_FACTS } from "../copy";

// The portal catalog in English: every page on the former employee's path (home, sign-in, sign-up, consent, account, privacy,
// about, the header, the footer and the error pages). Polish is ./pl.ts, typed against this file, so a missing key is a compile
// error. The developer pages are English-only (./developer.ts); the interview page has its own catalog (./interview.ts).
// Strings that state a precise fact about the system are asserted by tests so the facts and the words cannot drift apart. Plain
// text only: no markup, no interpolation of user data.

export const en = {
  site: {
    name: "Exit Interview Agent",
    description: "Give your exit interview to an AI interviewer, read the record it makes, and delete everything when you are done.",
    devBuild: "Development build: simulated interviews only. Nothing here is deployed, and no real personal data may be entered.",
    skipToContent: "Skip to content",
    loading: "Loading…",
  },
  nav: {
    label: "Main",
    home: "Home",
    interview: "Interview",
    account: "Account",
    signIn: "Sign in",
    signOut: "Sign out",
  },
  language: {
    label: "Language",
    // The switch names the OTHER language, in that language, so a person who cannot read the current one still finds it.
    switchTo: "Polski",
    switchToLang: "pl",
    switchHint: "Przełącz na polski",
    englishOnly: "This page is only in English.",
  },
  footer: {
    label: "More",
    privacy: "Privacy",
    about: "About this project",
    developers: "For developers",
  },
  landing: {
    title: "Give your exit interview to an AI",
    lead:
      "You left a job and want to say how it really was. An AI interviewer asks you about six topics, one at a time, in Polish " +
      "or English. At the end you get a short record of what you said and draft texts you can copy, change or throw away.",
    start: "Start your interview",
    startNote: "You sign in first, or create an account if you do not have one yet.",
    keptTitle: "What is kept, and what is not",
    kept:
      "Your account is your email address and password, and the terms you accepted. The conversation is sent to Anthropic, the " +
      "company behind the AI model, so it can reply; this site keeps it only in memory while the interview runs. When the " +
      "interview ends, your record and drafts stay readable for 30 minutes and are then wiped. You can delete everything sooner.",
    keptLink: "The privacy facts",
    stepsTitle: "How it works",
    steps: [
      "Sign in, or create an account with your email address.",
      "Choose the language and how long you worked there, then read what happens to your words.",
      "Talk with the AI interviewer in the chat. Stop whenever you want.",
      "Read your record and the draft texts, copy or download what you want, and delete the rest.",
    ],
    notTitle: "What this is not",
    not: [
      "Not a review site: nothing you say is published by this site.",
      "Not legal advice: the drafts are drafts, not facts.",
      "Not a check of your employment: nobody verifies where you worked.",
    ],
    aboutLink: "About this open-source project",
  },
  about: {
    title: "About this project",
    lead:
      "Exit Interview Agent is an open-source project. Besides the interview on this website, it can run the same structured " +
      "interview from your own AI client or from a command-line tool, with your own model. In that case this website runs no " +
      "model and never sees your interview; it only receives the final record if you choose to submit it.",
    pathsTitle: "If you use your own AI client or the command line",
    storedTitle: "What is stored",
    stored: [
      "Your account (email and credentials) and the consents you gave, held by the identity service.",
      "A submitted record: ratings per topic, short supporting quotes, the employer, and coarse bands. It has no account, name or email in it.",
      "A one-way marker per employer that stops the same account submitting twice. It holds no content and is removed automatically.",
      "While a ticket is unused: a hash of it, the account it was made for, and its expiry.",
    ],
    notStoredTitle: "What is not stored",
    notStored: [
      "The interview transcript. It stays in your AI client or on your computer.",
      "A link between your account and a submission. Nobody, including the operator, can list your submissions.",
      "Your receipt code in readable form, or any record of which account a code came from.",
      "Interview content, names or email addresses in logs.",
    ],
    recordsNote:
      "A submitted record is treated as personal data. Quotes can be recognisable to people who know you, so this service never " +
      "calls records anonymous.",
    nonGoalsTitle: "What this is not",
    nonGoals: [
      "Not a public review site: individual records are never published, only aggregates above a minimum number of records.",
      "Not a source of verified employment: verification is not implemented.",
      "Not a model host for the AI-client and command-line paths: you bring the model, through your AI client or your own API key or local model.",
    ],
    sourceTitle: "Source code",
    sourceLink: "The project repository on GitHub",
    sourceHref: "https://github.com/konradcinkusz/exit-interview-agent",
    developersLink: "Pages for developers",
  },
  developers: {
    title: "For developers",
    lead:
      "These pages are for people who run the interview from their own AI client or the command line, and for the employer " +
      "figures built from submitted records. You do not need any of them for the interview on this website.",
    links: [
      { href: "/connect", label: "Connect your AI client", what: "Add this service to Claude as a connector, so the interview runs in your own Claude conversation.", gated: false },
      { href: "/cli", label: "Use the command line", what: "Create a single-use ticket so the command-line tool can submit a record for you.", gated: true },
      { href: "/delete-submission", label: "Delete a submission", what: "Delete a record you submitted from your AI client or the command line, with its receipt code.", gated: false },
      { href: "/signals", label: "Employer signals", what: "Aggregated figures about employers, with their uncertainty. Never a ranking.", gated: true },
    ],
    englishOnly: "These pages are in English.",
  },
  devHints: {
    title: "Local development",
    intro: "Shown only because this web app runs with DEV_HINTS=1 (the local AppHost sets it). Never shown in a deployment.",
    email: "Test e-mail",
    password: "Test password",
    seed: "If signing in fails, first run “Seed test user (with credits)” from the … menu of postgres, authdb or interviewdb in the Aspire dashboard. It creates this account with interview credits.",
  },
  login: {
    title: "Sign in",
    introInterview: "Sign in to start your exit interview.",
    intro: "Sign in to your account.",
    email: "Email",
    password: "Password",
    submit: "Sign in",
    noAccount: "No account yet?",
    register: "Create one",
    twoFactorTitle: "Two-factor sign-in",
    twoFactorIntro: "Enter the 6-digit code from your authenticator app.",
    code: "Authenticator code",
    useRecovery: "Use a recovery code instead",
    useCode: "Use an authenticator code instead",
    recoveryCode: "Recovery code",
    recoveryHint: "A recovery code works once.",
    twoFactorSubmit: "Verify and sign in",
    twoFactorBack: "Start again",
    errors: {
      invalid_credentials: "Invalid email or password.",
      email_not_verified: "Verify your email address first. Use the link in the message we sent, or ask for a new one.",
      rate_limited: "Too many attempts. Try again in a minute.",
      identity_not_configured: "Sign-in is not configured in this environment.",
      invalid_code: "That code is not valid. Check the code and your device clock, then try again.",
      challenge_expired: "That sign-in has expired. Start again with your email and password.",
      locked: "This account is temporarily locked after too many failed attempts. Try again later.",
      generic: "Sign-in failed. Try again.",
    },
  },
  register: {
    title: "Create an account",
    intro: "An account is only a login. It is not linked to any employer, and it is not linked to what you submit.",
    email: "Email",
    password: "Password",
    passwordHint: "At least 8 characters.",
    terms: "I accept the Terms of Use",
    privacy: "I accept the Privacy Policy",
    version: "Version",
    submit: "Create account",
    haveAccount: "Already have an account?",
    signIn: "Sign in",
    created: "Account created. You can sign in now.",
    createdVerify: "Account created. Check your email for a link to verify your address, then sign in.",
    errors: {
      invalid: "The account could not be created. Check the email address and choose a longer or different password.",
      both_required: "Accept both documents to create an account.",
      rate_limited: "Too many attempts. Try again in a minute.",
      identity_not_configured: "Sign-up is not configured in this environment.",
      generic: "The account could not be created. Try again.",
    },
  },
  verifyEmail: {
    title: "Verify your email address",
    working: "Verifying…",
    done: "Your email address is verified. You can sign in now.",
    failed: "That link is invalid or has expired. Ask for a new one below.",
    missing: "Open the link from the verification email, or ask for a new one below.",
    resendTitle: "Send a new link",
    email: "Email",
    resend: "Send a new link",
    resent: "If that address needs verification, a new link has been sent.",
    rateLimited: "Too many attempts. Try again in a minute.",
    signIn: "Go to sign in",
    generic: "Something went wrong. Try again.",
  },
  consent: {
    title: "Before you continue",
    intro:
      "To use this service you need to accept the current versions of these documents. You see this page after you first sign in, " +
      "and again whenever one of them changes.",
    terms: "I accept the Terms of Use",
    privacy: "I accept the Privacy Policy",
    version: "Version",
    termsSummary: CONSENT_SUMMARY.terms,
    privacySummary: CONSENT_SUMMARY.privacy,
    accept: "Accept and continue",
    decline: "Decline and sign out",
    loadError: "Could not check your consent status. Try again.",
    saveError: "Could not record your consent. Try again.",
  },
  account: {
    title: "My account",
    signedIn: "You are signed in.",
    accountId: "Account ID",
    loadError: "Could not load your account. Try again later.",
    signOut: "Sign out",
    interviewTitle: "Your interview",
    interviewText: "Start, continue or delete your exit interview on the interview page.",
    interviewLink: "Go to the interview",
    exportTitle: "Export your data",
    exportIntro: "Download what the identity service holds about your account: profile, consents, sign-in methods and sessions.",
    exportNot:
      "Your submissions are not part of it. They are not linked to your account, so neither the identity service nor this website can find them.",
    exportButton: "Download my data (JSON)",
    deleteTitle: "Delete account",
    deleteLabel: "Delete account",
    password: "Password",
    passwordHint: "(not needed if you sign in with an external provider)",
    confirm: "Type DELETE to confirm",
    deleteSubmit: "Delete my account",
    deletion: DELETION_FACTS,
    errors: {
      password_required: "Enter your password to confirm.",
      invalid_password: "That password is not correct.",
      confirmation_required: "Type DELETE exactly to confirm.",
      unauthenticated: "Your session has ended. Sign in again.",
      identity_unavailable: "The identity service could not be reached. Nothing was deleted; try again.",
      generic: "Could not delete the account. Try again.",
    },
  },
  accountDeleted: {
    title: "Your account is closed",
    back: "Back to the start",
    deleteSubmission: "Delete a submission with its receipt code",
  },
  privacy: {
    title: "Privacy facts",
    lead: "The short version of the privacy design. The full design, with its limits, is linked at the bottom and is the authority.",
    interviewTitle: "The interview on this website",
    interview: [
      "The interviewer is an AI, and it tells you so at the start.",
      "Everything you type is sent to the AI model provider, Anthropic, to produce each reply. Anthropic's own terms and data rules apply to that.",
      "While the interview runs, the conversation is held in memory on the server. When it ends, the conversation is discarded; the transcript is not kept.",
      "When the interview ends, your record and draft texts stay readable for 30 minutes, then they are wiped. You can delete them sooner.",
      "This page keeps nothing in your browser's storage: reloading it empties the conversation and the result.",
    ],
    factsTitle: "Records submitted from your AI client or the command line",
    facts: [
      "The interview transcript stays on your side: in your AI client or on your computer. This service receives only the final record.",
      "A record has no account, name or email in it, but it is still personal data: quotes and the employer can point to a person in a small team. It is never called anonymous.",
      "Your account is not linked to your submissions. There is no list of what you submitted, and nobody can produce one.",
      "To stop the same account submitting twice for one employer, the service keeps a one-way marker per account and employer. It has no content and is deleted after a retention window.",
      "A receipt code deletes the submission it belongs to. It is shown once and cannot be recovered. A deletion answer never reveals whether a submission existed.",
      "Records are deleted automatically after a maximum age that the operator sets.",
      "Logs, traces and metrics hold no interview content, names or email addresses.",
      "Results are only ever shown as aggregates above a minimum number of records, with their uncertainty. Individual records are never published.",
    ],
    limitsTitle: "What this cannot promise",
    limits: [
      "An operator who has the database, the secret key for the markers and live traffic could, with effort, work out which account submitted for which employer. This is a limit of the design, stated openly.",
      "The AI client or provider you use sees your conversation under your own terms with them.",
      "Short records in small teams can be recognised by people who know the team.",
    ],
    cookiesTitle: "Cookies",
    cookies:
      "This site sets only the cookies it needs: your sign-in session (HttpOnly, not readable by page scripts) and, if you switch it, your language.",
    linksTitle: "The full documents",
    links: [
      { label: "Privacy design", href: "https://github.com/konradcinkusz/exit-interview-agent/blob/main/docs/privacy/DESIGN.md" },
      { label: "Threat model", href: "https://github.com/konradcinkusz/exit-interview-agent/blob/main/docs/security/THREAT-MODEL.md" },
      { label: "Open problems", href: "https://github.com/konradcinkusz/exit-interview-agent/blob/main/docs/OPEN-PROBLEMS.md" },
    ],
    linksNote: "These links open the project repository on GitHub. The documents are in English.",
  },
  errors: {
    notFoundTitle: "Page not found",
    notFound: "There is nothing at this address.",
    errorTitle: "Something went wrong",
    error: "The page could not be shown. Nothing you entered was saved. You can try again or go back to the start.",
    retry: "Try again",
    home: "Back to the start",
  },
} as const;

type DeepString<T> = T extends string
  ? string
  : T extends boolean
    ? boolean
    : T extends readonly (infer U)[]
      ? readonly DeepString<U>[]
      : { [K in keyof T]: DeepString<T[K]> };

/** The shape every language must provide: same keys, any strings. */
export type Messages = DeepString<typeof en>;
