import type { Metadata } from "next";
import { Suspense } from "react";
import { getMessages } from "@/lib/i18n";
import { ConsentForm } from "./ConsentForm";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getMessages()).t.consent.title };
}

export default async function Page() {
  const { t } = await getMessages();
  return (
    <div className="auth">
      <div className="card auth-card">
        <Suspense fallback={<p role="status">{t.site.loading}</p>}>
          <ConsentForm />
        </Suspense>
      </div>
    </div>
  );
}
