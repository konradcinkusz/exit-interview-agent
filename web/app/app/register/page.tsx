import type { Metadata } from "next";
import { Suspense } from "react";
import { getMessages } from "@/lib/i18n";
import { RegisterForm } from "./RegisterForm";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getMessages()).t.register.title };
}

export default async function Page() {
  const { t } = await getMessages();
  return (
    <div className="auth">
      <div className="card auth-card">
        <Suspense fallback={<p role="status">{t.site.loading}</p>}>
          <RegisterForm />
        </Suspense>
      </div>
    </div>
  );
}
