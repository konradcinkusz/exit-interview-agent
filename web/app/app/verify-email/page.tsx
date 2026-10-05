import type { Metadata } from "next";
import { Suspense } from "react";
import { m } from "@/lib/messages";
import { VerifyEmail } from "./VerifyEmail";

export const metadata: Metadata = { title: m.verifyEmail.title };

export default function VerifyEmailPage() {
  return (
    <Suspense fallback={<p>{m.site.loading}</p>}>
      <VerifyEmail />
    </Suspense>
  );
}
