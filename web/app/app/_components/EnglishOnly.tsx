import { getMessages } from "@/lib/i18n";

/**
 * Wraps a developer page, whose copy is English only (lib/messages/developer.ts). A visitor who chose Polish is told so in Polish,
 * and the content is marked `lang="en"` so a screen reader reads it with the right voice.
 */
export async function EnglishOnly({ children }: { children: React.ReactNode }) {
  const { locale, t } = await getMessages();
  if (locale === "en") return <>{children}</>;
  return (
    <>
      <p className="notice" data-testid="english-only">{t.language.englishOnly}</p>
      <div lang="en">{children}</div>
    </>
  );
}
