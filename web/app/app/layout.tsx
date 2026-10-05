import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Exit Interview Agent",
  description: "Structured exit interviews with privacy-preserving employer signals. Simulated data only.",
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <body>
        <main>{children}</main>
      </body>
    </html>
  );
}
