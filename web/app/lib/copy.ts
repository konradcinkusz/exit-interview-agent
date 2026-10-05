// User-facing copy that states a precise fact about the system. Kept in one place so the account page, the
// post-deletion page and the tests say the same thing (ADR-0013). Plain strings, no markup.

export const DELETION_FACTS = {
  headline: "What deleting your account does, and what it does not do",
  removes:
    "It removes your login. Your account is closed immediately and every session is ended; the identity service then " +
    "erases the account itself after its retention period.",
  notSubmissions:
    "It does not delete anything you submitted. Submissions are not linked to your account, by design: nobody, " +
    "including the operator of this service, can tell which submissions came from which account, so closing the " +
    "account cannot remove them.",
  receipt:
    "To remove a submission, use its receipt code. The code you were given when you submitted is the only way to " +
    "delete that submission, so keep it safe: a lost code cannot be recovered or replaced.",
  ledger:
    "The service also keeps a one-way marker per employer that stops the same account submitting twice. It holds no " +
    "content and no link to a submission, and it is removed automatically when its retention window ends.",
} as const;

export const CONSENT_SUMMARY = {
  terms: "The Terms of Use for this service.",
  privacy:
    "The Privacy Policy: this portal holds your account and your consents, your submissions are not linked to your " +
    "account, and no interview content, name or email address is written to logs.",
} as const;
