import Link from "next/link";

export default function Home() {
  return (
    <>
      <h1>Exit Interview Agent</h1>
      <p>
        An open-source AI agent for structured exit interviews with former employees. This portal holds your account,
        your consents and your own submissions. It runs no model: you bring your own.
      </p>
      <p className="muted">
        Development build: simulated interviews only. Nothing here is deployed, and no real personal data may be
        entered.
      </p>
      <p>
        <Link href="/login" data-testid="login-link">Sign in</Link> · <Link href="/account" data-testid="account-link">My account</Link>
      </p>
    </>
  );
}
