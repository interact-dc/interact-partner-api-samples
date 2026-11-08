using System.Text.Json;

namespace Interact.PartnerApi;

/// <summary>
/// Gets access tokens and keeps them until shortly before they run out: one
/// for your system (client credentials), and one per user (token exchange).
/// </summary>
internal sealed class TokenProvider(HttpClient http, AssertionSigner signer, Uri tokenUrl, TimeProvider time)
{
    private const string AssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";
    private const string TokenExchange = "urn:ietf:params:oauth:grant-type:token-exchange";
    private const string JwtTokenType = "urn:ietf:params:oauth:token-type:jwt";

    /// <summary>A token is replaced this long before it runs out, so a call never uses one that expires in flight.</summary>
    private static readonly TimeSpan RefreshBefore = TimeSpan.FromSeconds(60);

    private readonly Dictionary<string, (string Token, DateTimeOffset Expires)> _tokens = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<string> GetAsync(PartnerUser? user, CancellationToken ct)
    {
        var key = CacheKey(user);
        await _lock.WaitAsync(ct);
        try
        {
            if (_tokens.TryGetValue(key, out var cached) && time.GetUtcNow() < cached.Expires - RefreshBefore)
            {
                return cached.Token;
            }
            var fresh = await RequestAsync(user, ct);
            _tokens[key] = fresh;
            return fresh.Token;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Drop a token the API refused, so the next call gets a new one.</summary>
    public void Forget(PartnerUser? user)
    {
        _lock.Wait();
        try { _tokens.Remove(CacheKey(user)); }
        finally { _lock.Release(); }
    }

    private static string CacheKey(PartnerUser? user) => user is null ? "" : $"{user.Tenant}/{user.Subject}";

    private async Task<(string Token, DateTimeOffset Expires)> RequestAsync(PartnerUser? user, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["client_assertion_type"] = AssertionType,
            ["client_assertion"] = signer.ClientAssertion(),
        };
        if (user is null)
        {
            form["grant_type"] = "client_credentials";
        }
        else
        {
            form["grant_type"] = TokenExchange;
            form["subject_token"] = signer.SubjectToken(user);
            form["subject_token_type"] = JwtTokenType;
        }

        using var response = await http.PostAsync(tokenUrl, new FormUrlEncodedContent(form), ct);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var json = body.RootElement;
        if (!response.IsSuccessStatusCode)
        {
            throw new TokenRequestException(
                (int)response.StatusCode,
                json.TryGetProperty("error", out var error) ? error.GetString() ?? "unknown" : "unknown",
                json.TryGetProperty("error_description", out var description) ? description.GetString() : null,
                RequestIds.From(response));
        }
        var expiresIn = TimeSpan.FromSeconds(json.GetProperty("expires_in").GetInt32());
        return (json.GetProperty("access_token").GetString()!, time.GetUtcNow() + expiresIn);
    }
}

internal static class RequestIds
{
    public static string? From(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Request-ID", out var values) ? values.FirstOrDefault() : null;
}
