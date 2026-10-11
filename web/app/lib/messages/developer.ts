// The developer and analyst pages (Connect your AI client, Use the CLI, Delete a submission, Employer signals), in English only.
// They are reached from the footer and the "For developers" page, not from the former employee's path, so they are not part of
// the translated portal catalog (./en.ts, ./pl.ts). A Polish visitor sees a one-line note that the page is in English
// (app/_components/EnglishOnly.tsx). Strings that state a precise fact are asserted by tests, as in ./en.ts.

export const developer = {
  site: { loading: "Loading…" },
  connect: {
    title: "Connect your AI client",
    lead:
      "You can run the interview inside Claude. Claude is the AI client that is supported today; other clients are future work. " +
      "The interview happens in your conversation with Claude, under your own Claude account and its terms.",
    urlTitle: "The connector address",
    urlIntro: "Add this address as a custom connector in Claude:",
    urlMissing:
      "The connector is not set up in this environment, so there is no address to show. The operator turns it on by giving the " +
      "web app a public HTTPS address for the MCP endpoint.",
    copy: "Copy address",
    copied: "Copied",
    copyFailed: "Copy failed. Select the address and copy it yourself.",
    stepsTitle: "How to add it",
    steps: [
      "In Claude, open Settings, then Connectors.",
      "Choose to add a custom connector and paste the address above.",
      "If the operator gave you a client ID and secret, enter them under Advanced settings. They are operator configuration, not part of your account.",
      "Sign in with the same account you use on this website and approve the connection.",
      "Start a new conversation and ask Claude to run an exit interview with the connector enabled.",
    ],
    plansNote: "Which Claude plans allow custom connectors is decided by Anthropic, not by this project.",
    canTitle: "What the connector can do",
    can: [
      "Give Claude the interview protocol: how to open, which topics to cover, how to ask without leading, when to stop.",
      "Accept one structured record at the end, if you agree to submit it.",
    ],
    cannotTitle: "What the connector cannot do",
    cannot: [
      "See your conversation. The server receives only the record that Claude submits, never the transcript.",
      "Read, list or change records. There is no way to look up what you submitted.",
      "Link a submission to your account: the record carries no account, and the receipt code is the only handle.",
      "Verify that your quotes are word for word, or that you worked where you say you did.",
    ],
    privacyNote:
      "Your conversation is visible to Claude and to Anthropic under your own agreement with them. Do not name individual people. " +
      "After a submission, Claude shows you a receipt code once: keep it, because it is the only way to delete that submission.",
    privacyLink: "Privacy facts",
    deleteLink: "Delete a submission",
    // There is no documentation site yet, so the runbook is linked where it lives: the project repository on GitHub, the same
    // place the privacy page links to (ADR-0070). It is written for whoever runs the deployment, and says what was not verified.
    runbookNote: "Running this deployment yourself? The operator runbook, with what was and was not verified, is in the project repository:",
    runbookLabel: "Connect Claude: operator runbook",
    runbookHref: "https://github.com/konradcinkusz/exit-interview-agent/blob/main/docs/guides/connect-claude.md",
    runbookPath: "docs/guides/connect-claude.md",
    config: { loading: "Loading…", error: "The connector address could not be loaded." },
  },
  cli: {
    title: "Use the command line",
    lead:
      "The command-line tool runs the whole interview on your computer with your own API key or a local model. It sends " +
      "only the final record. To submit, it needs a ticket from this page.",
    ticketTitle: "Submission ticket",
    ticketFacts: [
      "Single use: the first successful submission with it uses it up.",
      "Short-lived: the countdown shows when it stops working; after that, make another.",
      "Not tied to any employer or to a record. It only proves that a signed-in account made it.",
      "A password-like secret: anyone who has it can use it once. Do not paste it into a chat, a ticket tracker or a shared file.",
    ],
    shownOnce: "It is shown once. This page does not store it and cannot show it again: reloading or leaving the page clears it.",
    create: "Create a ticket",
    creating: "Creating…",
    yourTicket: "Your ticket",
    copy: "Copy ticket",
    copied: "Copied. Paste it into the CLI now; it is cleared from the clipboard only when you copy something else.",
    copyFailed: "Copy failed. Select the ticket and copy it yourself.",
    expiresIn: "Stops working in",
    expired: "This ticket has expired and was cleared. Create a new one.",
    clear: "Clear it now",
    cleared: "Ticket cleared from this page.",
    limitTitle: "No ticket created",
    errors: {
      TICKET_LIMIT:
        "You already have the maximum number of unused tickets, or you made too many in the last hour. Use one or wait for one to expire, then try again.",
      rate_limited: "Too many requests. Wait a little and try again.",
      unauthenticated: "Your session has ended. Sign in again.",
      consent_required: "Accept the current Terms and Privacy Policy first.",
      unavailable: "The service could not be reached. Nothing was created; try again.",
      generic: "A ticket could not be created. Try again.",
    },
    waitSeconds: "Try again in about",
    seconds: "seconds",
    commandsTitle: "The CLI today",
    commandsIntro: "These are the commands the command-line tool has in this version:",
    commands: [
      { command: "exit-interview interview --provider <p> --model <m> --out <dir>", what: "runs a real interview with your own model or API key and writes the record to a file; nothing is sent anywhere" },
      { command: "exit-interview submit --record <file> --server <address>", what: "checks the record on your computer, shows exactly what will be sent, asks you to type \"submit\", then asks for the ticket and sends it" },
      { command: "exit-interview delete-receipt --server <address>", what: "deletes a submission with its receipt code, which it asks for" },
      { command: "exit-interview personas", what: "lists the simulated interviewees" },
      { command: "exit-interview demo --persona <id> [--seed <n>] [--out <dir>]", what: "runs a full interview offline against a simulated person and prints the record" },
      { command: "exit-interview --help", what: "shows the usage" },
    ],
    commandsNote:
      "Never put the ticket or a receipt code on the command line: the tool has no option for either, because command lines end up in process lists " +
      "and shell history. It asks for the ticket with a hidden prompt, or reads it from the EXIT_INTERVIEW_TICKET environment variable or from standard input. " +
      "There is no default server address: give the address of this site, over https. After a submission the tool shows a receipt code once. " +
      "It is the only way to delete the record, and nobody can look it up for you.",
  },
  deleteSubmission: {
    title: "Delete a submission",
    lead:
      "Enter the receipt code you were given when you submitted. You do not need to sign in: the code is the only thing that " +
      "identifies a submission, because submissions are not linked to accounts.",
    label: "Receipt code",
    hint: "46 characters. Letters, digits, hyphens and underscores.",
    submit: "Delete this submission",
    working: "Deleting…",
    // The uniform answer (ADR-0029). Deliberately does not say whether a submission existed.
    uniformAnswer: "If a submission with this receipt code existed, it is deleted now.",
    uniformDetail:
      "This answer is the same for every correctly typed code, so it does not tell you whether a submission existed. " +
      "Deletion cannot be undone.",
    // AGGREGATION section 8: the receipt-deletion screen says what happens to published figures. Always shown, so it does not depend on the outcome.
    publishedFigures: "Deleting your record removes it from the stored data now. Published figures drop it at the next update.",
    lostCode: "A lost code cannot be recovered or replaced, and nobody can look up your submissions without it.",
    errors: {
      INVALID_RECEIPT_CODE:
        "That is not a valid receipt code. Check for a missed or extra character. Nothing was deleted and no request about a submission was made.",
      rate_limited: "Too many attempts. Wait a minute and try again.",
      unavailable: "The service could not be reached. Nothing was deleted; try again.",
      generic: "Something went wrong. Nothing was confirmed; try again.",
    },
    waitSeconds: "Try again in about",
    seconds: "seconds",
    accountNote: "Closing your account does not delete submissions. Only the receipt code does.",
    again: "Delete another",
  },
  signals: {
    title: "Employer signals",
    lead: "Aggregated figures about employers, built from records submitted by users of this tool.",
    // AGGREGATION section 8: every aggregate carries this, and the snapshot line next to the date.
    aggregatedNote: "Aggregated from records submitted by users of this tool. Employment is claimed, not verified.",
    snapshot: "Updated {date}. Figures change once per update, not when someone submits. A record deleted after this date is still counted until the next update.",
    snapshotInterval: "A new update is published every {hours} hours.",
    explainerTitle: "How to read these figures",
    explainer: [
      "Ratings are on a scale from 1 (lowest) to 5 (highest). Each topic stands alone: there is no overall score for an employer and no average across topics.",
      "Every figure comes with its 95% interval and the number of ratings it rests on, on the same line. The interval is the range the figure could plausibly be in. A wide interval means early, not wrong.",
      "Reliability says how much the figure can move as more ratings arrive. Coverage says what share of the respondents rated the topic at all: a figure with low coverage speaks for few of them.",
      "Two figures whose intervals overlap are not different. This tool does not compare employers, and a figure here is not a verdict.",
      "Some figures are hidden to protect small groups. The page does not say which, or how many.",
    ],
    listTitle: "Employers",
    listOrder: "Employers are listed alphabetically. This tool does not rank employers.",
    listLabel: "Employers with figures to show",
    empty: "No employer has enough responses to show yet.",
    emptyDetail: "An employer appears here only when at least one figure rests on enough ratings to be shown. This page does not say how many employers or responses there are.",
    previous: "Previous page",
    next: "Next page",
    pageLabel: "Page {page}",
    paging: "Pages",
    employerTitle: "Employer signals",
    employerLabel: "Employer reference",
    backToList: "Back to the employer list",
    nothing: "There is nothing to show for this employer.",
    nothingDetail: "Either this tool has no figures it can show for this employer, or the reference is not one it knows. The page cannot tell you which.",
    respondents: "Respondents",
    respondentsNote: "A range, not an exact count.",
    minimumNote: "A figure is shown only when it rests on at least {k} ratings.",
    notEnough: "Not enough responses to show this topic.",
    statLine: "{mean} ({level}% interval {lower}-{upper}), {n} ratings",
    wideInterval: "A wide interval means early, not wrong.",
    reliabilityLabel: "Reliability",
    reliability: {
      low: "low: based on few ratings, so the figure can move a lot.",
      moderate: "moderate: based on a fair number of ratings.",
      high: "high: based on many ratings.",
    },
    coverageLabel: "Coverage",
    coverage: {
      high: "high: the share of respondents who rated this topic.",
      medium: "medium: the share of respondents who rated this topic.",
      low: "low: the share of respondents who rated this topic.",
    },
    distributionCaption: "How the ratings were spread",
    distributionGroup: "Rating group",
    distributionCount: "Ratings",
    distribution: { low: "Low (1-2)", mid: "Middle (3)", high: "High (4-5)" } as Record<string, string>,
    verificationTitle: "Employment checks",
    verificationNote: "Employment is claimed, not verified.",
    verificationCaption: "Ratings by employment check",
    verificationGroup: "Employment check",
    verification: { unchecked: "Not checked", unverified: "Checked, not confirmed", verified: "Confirmed" } as Record<string, string>,
    cutsTitle: "Breakdowns",
    cutsNote: "One breakdown at a time: tenure, seniority and function are never combined.",
    cutSuppressed: "This breakdown is hidden to protect small groups.",
    cutNone: "No ratings.",
    cutBandSuppressed: "Hidden to protect small groups.",
    cutGroup: "Group",
    cutFigures: "Figures",
    cutCaption: "Breakdown by {dimension}",
    dimensions: { tenure: "tenure", seniority: "seniority", function: "function" } as Record<string, string>,
    topics: {
      onboarding: "Onboarding",
      management: "Management",
      growth: "Growth",
      pay_vs_promises: "Pay versus promises",
      culture: "Culture",
      reason_for_leaving: "Reason for leaving",
    } as Record<string, string>,
    bands: {
      lt_6m: "Less than 6 months",
      "6m_1y": "6 to 12 months",
      "1y_3y": "1 to 3 years",
      "3y_5y": "3 to 5 years",
      "5y_10y": "5 to 10 years",
      gt_10y: "More than 10 years",
      junior: "Junior",
      mid: "Mid-level",
      senior: "Senior",
      management: "Management",
      engineering: "Engineering",
      product_design: "Product and design",
      sales_marketing: "Sales and marketing",
      operations_support: "Operations and support",
      corporate_functions: "Corporate functions",
      other: "Other",
    } as Record<string, string>,
    errors: {
      rate_limited: "Too many requests. Nothing is shown for now, and this page will not retry by itself.",
      unauthenticated: "Your session has ended. Sign in again.",
      consent_required: "Accept the current Terms and Privacy Policy first.",
      unavailable: "The signals could not be loaded. Nothing is shown. Try again later.",
      generic: "The signals could not be loaded. Nothing is shown.",
    },
    waitPrefix: "You can try again in about",
    waitSuffix: "seconds.",
    retry: "Try again",
    retryWaiting: "Waiting",
  },
} as const;
