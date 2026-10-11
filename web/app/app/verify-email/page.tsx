import type { Metadata } from "next";
import { Suspense } from "react";
import { getMessages } from "@/lib/i18n";
import { VerifyEmail } from "./VerifyEmail";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getMessages()).t.verifyEmail.title };
}

export default async function Page() {
  const { t } = await getMessages();
  return (
    <div className="auth">
      <div className="card auth-card">
        <Suspense fallback={<p role="status">{t.site.loading}</p>}>
          <VerifyEmail />
        </Suspense>
      </div>
    </div>
  );
}
