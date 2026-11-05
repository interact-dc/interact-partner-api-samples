using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Interact.PartnerApi;

/// <summary>An event Interact sent your webhook address.</summary>
/// <param name="Id">The <c>webhook-id</c>: the same on every retry. De-duplicate on it.</param>
/// <param name="Type"><c>run.completed</c>, <c>run.failed</c>, <c>run.paused</c>, <c>credits.low</c> or <c>ping</c>.</param>
/// <param name="Data">What happened, and where to read it (<c>path</c>, under the API).</param>
public sealed record WebhookEvent(string Id, string Type, DateTimeOffset Timestamp, JsonElement Data);

/// <summary>A delivery that isn't from Interact, or is too old to trust: answer it with a 4xx.</summary>
public sealed class WebhookVerificationException(string message) : Exception(message);

/// <summary>
/// Checks a webhook really is from Interact (the Standard Webhooks specification), before your
/// receiver acts on it. Verify against the raw body, exactly as it arrived: parsing and
/// re-serialising it first changes the bytes and breaks the signature.
/// </summary>
/// <param name="secret">Your signing secret, <c>whsec_...</c>, from <c>POST /v1/webhooks/secret</c>.</param>
public sealed class WebhookVerifier(string secret, TimeProvider? time = null)
{
    /// <summary>How far a delivery's timestamp may be from now: older ones may be replays.</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    private readonly byte[] _key = Convert.FromBase64String(
        secret.StartsWith("whsec_", StringComparison.Ordinal) ? secret["whsec_".Length..] : secret);
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>The event, if the headers prove it is Interact's; otherwise a <see cref="WebhookVerificationException"/>.</summary>
    /// <param name="id">The <c>webhook-id</c> header.</param>
    /// <param name="timestamp">The <c>webhook-timestamp</c> header.</param>
    /// <param name="signatures">The <c>webhook-signature</c> header: one or more <c>v1,...</c>, space-separated.</param>
    /// <param name="body">The request body, as it arrived.</param>
    public WebhookEvent Verify(string? id, string? timestamp, string? signatures, byte[] body)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(signatures))
            throw new WebhookVerificationException("Missing webhook-id, webhook-timestamp or webhook-signature.");
        if (!long.TryParse(timestamp, out var seconds))
            throw new WebhookVerificationException("webhook-timestamp is not a number.");
        var sent = DateTimeOffset.FromUnixTimeSeconds(seconds);
        if ((_time.GetUtcNow() - sent).Duration() > Tolerance)
            throw new WebhookVerificationException("The delivery is too old, or from the future.");

        byte[] signed = [.. Encoding.UTF8.GetBytes($"{id}.{timestamp}."), .. body];
        var expected = HMACSHA256.HashData(_key, signed);
        var matched = signatures.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(s => s.StartsWith("v1,", StringComparison.Ordinal))
            .Any(s =>
            {
                try { return CryptographicOperations.FixedTimeEquals(expected, Convert.FromBase64String(s[3..])); }
                catch (FormatException) { return false; }
            });
        if (!matched) throw new WebhookVerificationException("No signature matches.");

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        return new WebhookEvent(id, root.GetProperty("type").GetString() ?? "",
                                root.GetProperty("timestamp").GetDateTimeOffset(), root.GetProperty("data").Clone());
    }
}
