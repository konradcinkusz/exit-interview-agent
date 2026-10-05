import Link from "next/link";
import { m } from "@/lib/messages";

const L = m.landing;

function List({ items }: { items: readonly string[] }) {
  return <ul className="plain">{items.map((i) => <li key={i}>{i}</li>)}</ul>;
}

export default function Home() {
  return (
    <>
      <h1>{L.title}</h1>
      <p>{L.lead}</p>
      <p className="notice warn">{m.site.devBuild}</p>

      <h2>{L.whatTitle}</h2>
      <List items={L.what} />
      <h2>{L.storedTitle}</h2>
      <List items={L.stored} />
      <h2>{L.notStoredTitle}</h2>
      <List items={L.notStored} />
      <p className="notice">{L.recordsNote}</p>
      <h2>{L.nonGoalsTitle}</h2>
      <List items={L.nonGoals} />

      <h2>{L.ctaTitle}</h2>
      <p>
        <Link className="button" href="/connect" data-testid="connect-link">{L.ctaConnect}</Link>
        <Link className="button secondary" href="/cli" prefetch={false} data-testid="cli-link">{L.ctaCli}</Link>
        <Link className="button secondary" href="/delete-submission">{L.ctaDelete}</Link>
      </p>
      <p>
        <Link href="/login" data-testid="login-link">{L.ctaSignIn}</Link> · <Link href="/register">{L.ctaRegister}</Link> ·{" "}
        <Link href="/privacy">{L.ctaPrivacy}</Link> · <Link href="/account" prefetch={false} data-testid="account-link">{m.nav.account}</Link>
      </p>
    </>
  );
}
