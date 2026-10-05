using System.Globalization;
using ExitInterviewAgent.Contracts;
using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.Cli;

/// <summary>
/// Plain-language, actionable wording for every outcome of a submission or a receipt deletion. Nothing here prints a secret, a
/// server-supplied sentence, or any text of the record: only fixed wording plus values that were already reduced to a code or a schema path.
/// </summary>
internal static class SubmitMessages
{
    public const string WebPanelHint = "the web panel's /cli page";

    public static IReadOnlyList<string> Explain(CallResult.Rejected r)
    {
        var lines = new List<string>();
        switch (r.Code)
        {
            case SubmissionCodes.TicketInvalid:
                lines.Add("The server did not accept the ticket. It may be wrong, expired (tickets last 15 to 20 minutes) or already used; the server deliberately does not say which.");
                lines.Add($"Mint a new ticket on {WebPanelHint} and run the command again. Your record was not stored.");
                break;
            case SubmissionCodes.EmploymentNotVerified:
                lines.Add("The server could not confirm that your account is linked to the employer named in the record, so it did not store the record.");
                lines.Add("Check the 'employerRef' in the record file. Your ticket was not used up.");
                break;
            case SubmissionCodes.AlreadySubmitted:
                lines.Add("Your account has already submitted a record for this employer, and the service accepts one per account and employer for a limited period.");
                lines.Add("Deleting the earlier record with its receipt code does not reopen that: the service cannot link the two. Nothing was stored this time. Your ticket was not used up.");
                break;
            case SubmissionCodes.InterviewIdTaken:
                lines.Add("A record with this interview id is already stored. Most likely an earlier attempt with this same file went through and you never saw the answer.");
                lines.Add("The receipt code of that attempt is not shown twice and cannot be recovered. Nothing new was stored. Your ticket was not used up.");
                break;
            case SubmissionCodes.PayloadTooLarge:
                lines.Add("The server says the record is too large. A record written by 'exit-interview interview --out' is well under the limit: is this the right file?");
                break;
            case RecordErrorCodes.NotJson or RecordErrorCodes.DuplicateKey or RecordErrorCodes.NestingTooDeep:
                lines.Add($"The server could not read the file as a record ({r.Code}). Use the record.json that 'exit-interview interview --out <dir>' wrote, unedited apart from fixes this tool asked for.");
                break;
            case SubmissionCodes.AiNotDisclosed:
                lines.Add("The record says the interviewee was not told the interviewer is an AI. The service stores only interviews where that was disclosed.");
                break;
            case SubmissionCodes.PiiDetected:
                lines.Add("The server's personal-data check found what looks like " + (r.Kinds.Count > 0 ? string.Join(", ", r.Kinds.Select(KindWords)) : "personal data") + " in the record. Nothing was stored.");
                lines.Add("The server does not say which quote. Replace the matching words in the quotes of the record file with a placeholder such as [PERSON] and submit again; 'exit-interview submit' runs the same kind of check here first and names the fields. Your ticket was not used up.");
                break;
            case SubmissionCodes.PiiCheckFailed:
                lines.Add("The server's personal-data check could not finish, so it refused to store anything (it fails closed). Try again in a few minutes. Your ticket was not used up.");
                break;
            case SubmissionCodes.RateLimited or SubmissionCodes.TicketLimit:
                lines.Add(RateLimitText(r));
                break;
            case SubmissionCodes.InvalidReceiptCode:
                lines.Add("That is not a well-formed receipt code (wrong length or characters, or a typo). The server did not look anything up. Check the code and try again.");
                break;
            default:
                if (r.Status == 429) lines.Add(RateLimitText(r));
                else if (r.Status is 404 or 405) lines.Add("The server does not have this endpoint. The address is probably not an exit-interview service, or is a different version. Check the server address.");
                else if (r.Status >= 500) lines.Add($"The server reported an internal problem (HTTP {r.Status.ToString(CultureInfo.InvariantCulture)}). Try again later. If this was a submission, it is not certain whether it was stored.");
                else if (r.Errors.Count > 0 || RecordErrorCodes.All.Contains(r.Code))
                {
                    lines.Add("The server rejected the record's format. Nothing was stored. Your ticket was not used up.");
                    foreach (var e in r.Errors.Take(10)) lines.Add($"  - {Describe(e.Code)}{(e.Path.Length > 0 ? " at " + e.Path : string.Empty)}");
                    if (r.Errors.Count == 0) lines.Add($"  - {Describe(r.Code)}");
                    lines.Add("This usually means the file was edited, or came from a different version of this tool.");
                }
                else lines.Add($"The server refused the request (HTTP {r.Status.ToString(CultureInfo.InvariantCulture)}, code {r.Code}). Nothing is known to be stored.");
                break;
        }
        return lines;
    }

    public static IReadOnlyList<string> Explain(CallResult.Failed f, ServerUrl server, bool submission)
    {
        var unknown = submission
            ? "The server may or may not have received the record. If it did, you will not get the receipt code, and sending the same file again would be refused as already stored."
            : "The server may or may not have processed the request. Deleting is safe to repeat.";
        return f.Reason switch
        {
            Failure.DnsFailed => [$"Could not find the server {server.Display}: its name did not resolve. Nothing was sent. Check the address and your network."],
            Failure.ConnectFailed => [$"Could not connect to {server.Display}. Nothing was sent (the CLI tried twice). Check the address, that the service is up, and your network."],
            Failure.TlsFailed => [$"The secure connection to {server.Display} could not be set up (certificate or protocol problem). Nothing was sent. The CLI has no option to skip certificate checks."],
            Failure.TimedOut => ["The server did not answer in time (30 seconds).", unknown],
            Failure.Cancelled => ["Cancelled.", unknown],
            Failure.Redirected => ["The server answered with a redirect. The CLI never follows redirects: one would hand your secret to whatever address it names.", "Check that the server address is exactly the deployment's own https address. Nothing was stored."],
            Failure.ResponseTooLarge => ["The server's answer was far larger than this protocol produces, so it was discarded. The address is probably not an exit-interview service.", unknown],
            Failure.BadResponse => ["The server answered, but not in the form this protocol uses. The address is probably not an exit-interview service, or a different version.", unknown],
            _ => ["The connection broke during the exchange.", unknown],
        };
    }

    private static string RateLimitText(CallResult.Rejected r)
    {
        var wait = r.RetryAfter is { } w ? $" Wait about {Math.Max(1, (int)Math.Ceiling(w.TotalSeconds)).ToString(CultureInfo.InvariantCulture)} seconds and run the command again." : " Wait a minute and run the command again.";
        return "The server is receiving too many requests from your network address (or in total) and asked you to slow down." + wait + " Nothing was stored; your ticket was not used up.";
    }

    public static string KindWords(string kind) => kind switch
    {
        "Email" => "an email address",
        "Url" => "a web address",
        "IpAddress" => "an IP address",
        "NationalId" => "a national or tax id number",
        "EmployeeId" => "an employee id",
        "Phone" => "a phone number",
        "Handle" => "a social-media handle",
        "PersonName" => "a person's name",
        _ => "personal data",
    };

    /// <summary>A short reading of a record-library code. Anything not listed falls back to the code itself.</summary>
    public static string Describe(string code) => code switch
    {
        RecordErrorCodes.UnsupportedSchemaVersion => "schema version not supported",
        RecordErrorCodes.MissingField => "a required field is missing",
        RecordErrorCodes.UnknownField => "a field the schema does not have",
        RecordErrorCodes.WrongType => "wrong type of value",
        RecordErrorCodes.ValueNotAllowed => "a value that is not allowed",
        RecordErrorCodes.BadFormat => "wrong format",
        RecordErrorCodes.OutOfRange => "value out of range",
        RecordErrorCodes.LengthLimit => "too long or too many",
        RecordErrorCodes.TopicInconsistent => "a topic whose status and content disagree",
        RecordErrorCodes.PiiNotMasked => "the record does not say personal data was masked",
        RecordErrorCodes.AiNotDisclosed => "AI interviewer not disclosed",
        RecordErrorCodes.ValidationTimeout => "validation took too long",
        RecordErrorCodes.QuoteNotVerbatim => "a quote that was not taken from the transcript",
        RecordErrorCodes.PayloadTooLarge => "file too large",
        RecordErrorCodes.NotJson => "not JSON",
        RecordErrorCodes.DuplicateKey => "a field given twice",
        RecordErrorCodes.NestingTooDeep => "nested too deeply",
        _ => code,
    };
}
