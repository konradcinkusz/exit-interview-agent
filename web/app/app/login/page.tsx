import type { Metadata } from "next";
import { Suspense } from "react";
import { m } from "@/lib/messages";
import { LoginForm } from "./LoginForm";

export const metadata: Metadata = { title: m.login.title };

export default function LoginPage() {
  return (
    <Suspense fallback={<p>{m.site.loading}</p>}>
      <LoginForm />
    </Suspense>
  );
}
