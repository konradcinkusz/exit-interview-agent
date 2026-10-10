namespace ExitInterviewAgent.InterviewService.Interviews;

/// <summary>
/// The legal notice printed under the tiles. The CLI owns the wording (<c>TileRenderer.NoticePl</c>, <c>NoticeEn</c>); this copy is
/// kept equal to it by a test. Moving the wording into the agent library is the clean fix and is left for a later task.
/// </summary>
public static class InterviewNotice
{
    public const string Pl = "To są propozycje tekstów, nie fakty. Program niczego nie weryfikuje i niczego nie publikuje. Za treść, którą opublikujesz, odpowiadasz Ty; publikacja opinii o pracodawcy może mieć skutki prawne. Przeczytaj i zmień każdy tekst, zanim go użyjesz. To nie jest porada prawna. Limity długości są orientacyjne; sprawdź aktualne zasady platformy. Nazwę firmy wstaw sam albo zostaw [FIRMA].";

    public const string En = "These are draft texts, not facts. The program verifies nothing and publishes nothing. You are responsible for any text you publish; publishing an opinion about an employer can have legal consequences. Read and change every text before you use it. This is not legal advice. Length limits are approximate; check the platform's current rules. Insert the company name yourself or leave [COMPANY].";

    public static string For(string language) => language == "pl" ? Pl : En;
}
