import type { Metadata } from "next";
import { Suspense } from "react";
import { getMessages } from "@/lib/i18n";
import { devHints } from "@/lib/runtime-config";
import { DevHintsBox } from "./DevHintsBox";
import { LoginForm } from "./LoginForm";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getMessages()).t.login.title };
}

export default async function LoginPage() {
  const { t } = await getMessages();
  return (
    <>
      <Suspense fallback={<p>{t.site.loading}</p>}>
        <LoginForm />
      </Suspense>
      <DevHintsBox hints={devHints()} t={t.devHints} />
    </>
  );
}
