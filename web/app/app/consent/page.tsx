import type { Metadata } from "next";
import { Suspense } from "react";
import { m } from "@/lib/messages";
import { ConsentForm } from "./ConsentForm";

export const metadata: Metadata = { title: m.consent.title };

export default function ConsentPage() {
  return (
    <Suspense fallback={<p>{m.site.loading}</p>}>
      <ConsentForm />
    </Suspense>
  );
}
