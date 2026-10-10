using System.Text.Json;

namespace ExitInterviewAgent.InterviewService.Billing;

/// <summary>
/// Turns a signed, checkout-shaped event into the internal vocabulary (PAYMENTS-AND-MONETIZATION §4). Only a paid checkout
/// completion whose quantity (1..10), currency and amount match what the service itself sold becomes a purchase. Everything else
/// is <see cref="PaymentEventKind.Ignored"/> with a reason that names the rule, never the value. The fake and the Stripe provider
/// share this code, so the fake tests the production classification.
/// </summary>
public static class WebhookEventParser
{
    public const string CheckoutCompleted = "checkout.session.completed";
    public const int MaxQuantity = 10;

    /// <summary>Reads the event. Throws <see cref="InvalidWebhookException"/> (<c>invalid_request</c>) when the body is not an event at all.</summary>
    public static PaymentEvent Parse(ReadOnlySpan<byte> body, BillingOptions options)
    {
        using var doc = ReadDocument(body);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !TryString(root, "id", out var eventId) || !TryString(root, "type", out var type))
        {
            throw new InvalidWebhookException(InvalidRequest);
        }

        if (type != CheckoutCompleted)
        {
            return Ignored(eventId, "unhandled_type");
        }

        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("object", out var session) || session.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidWebhookException(InvalidRequest);
        }

        var paid = TryString(session, "payment_status", out var status) && status == "paid";
        if (!paid) return Ignored(eventId, "not_paid");

        var quantity = ReadQuantity(session);
        if (quantity is < 1 or > MaxQuantity) return Ignored(eventId, "quantity_out_of_range");

        if (!TryString(session, "currency", out var currency) || !string.Equals(currency, options.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return Ignored(eventId, "currency_mismatch");
        }

        if (!session.TryGetProperty("amount_total", out var amountElement) || amountElement.ValueKind != JsonValueKind.Number
            || !amountElement.TryGetInt64(out var amount) || amount != (long)options.PriceMinorUnits * quantity)
        {
            return Ignored(eventId, "amount_mismatch");
        }

        if (!TryString(session, "client_reference_id", out var account) || account.Length == 0 || account.Length > 256)
        {
            return Ignored(eventId, "missing_account");
        }

        return new PaymentEvent(eventId, PaymentEventKind.Purchase, account, quantity, amount, currency.ToLowerInvariant());
    }

    /// <summary>The quantity is set by the service at checkout (metadata), so the signed body carries it; a missing or odd value is 0.</summary>
    private static int ReadQuantity(JsonElement session)
    {
        if (!session.TryGetProperty("metadata", out var metadata) || metadata.ValueKind != JsonValueKind.Object) return 0;
        if (!metadata.TryGetProperty("credits", out var credits) || credits.ValueKind != JsonValueKind.String) return 0;
        return int.TryParse(credits.GetString(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    private static PaymentEvent Ignored(string eventId, string reason) => new(eventId, PaymentEventKind.Ignored, IgnoredReason: reason);

    private static bool TryString(JsonElement element, string name, out string value)
    {
        value = "";
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String) return false;
        value = property.GetString() ?? "";
        return value.Length > 0;
    }

    private static JsonDocument ReadDocument(ReadOnlySpan<byte> body)
    {
        try
        {
            return JsonDocument.Parse(body.ToArray());
        }
        catch (JsonException)
        {
            // The message would name a position in the payer's body; the code is all that leaves this method.
            throw new InvalidWebhookException(InvalidRequest);
        }
    }

    public const string InvalidRequest = "invalid_request";
}
