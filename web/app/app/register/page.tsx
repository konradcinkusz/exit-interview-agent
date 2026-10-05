import type { Metadata } from "next";
import { m } from "@/lib/messages";
import { RegisterForm } from "./RegisterForm";

export const metadata: Metadata = { title: m.register.title };

export default function RegisterPage() {
  return <RegisterForm />;
}
