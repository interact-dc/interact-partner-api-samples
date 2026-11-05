using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Interact.PartnerApi;

/// <param name="BaseAddress">Interact's partner host, for example <c>https://partners.interactdc.com/</c>.</param>
/// <param name="ClientId">The client id Interact gave you when registering your public key.</param>
/// <param name="SigningKey">Your private key. Its public half is the one you sent Interact.</param>
public sealed record PartnerApiOptions(Uri BaseAddress, string ClientId, ECDsa SigningKey);

/// <summary>
/// A client for the Interact Partner API.
///
/// It gets and reuses access tokens for you, as your system or as one of your
/// users. A token the API refuses is replaced once. A rate-limited call waits
/// as long as the API says and is tried again, up to three times. Every POST
/// carries an Idempotency-Key, the same on each retry, so a retried POST never
/// does its work twice. Errors are raised as <see cref="PartnerApiException"/>.
/// </summary>
public sealed class PartnerApiClient
{
    private const int MaxTries = 3;

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly Uri _api;
    private readonly TokenProvider _tokens;
    private readonly Func<TimeSpan, CancellationToken, Task> _wait;

    /// <param name="wait">How to wait before a retry; tests pass one that doesn't.</param>
    public PartnerApiClient(PartnerApiOptions options, HttpClient? http = null, TimeProvider? time = null,
                            Func<TimeSpan, CancellationToken, Task>? wait = null)
    {
        time ??= TimeProvider.System;
        _http = http ?? new HttpClient();
        _api = new Uri(options.BaseAddress, "v1/");
        var tokenUrl = new Uri(options.BaseAddress, "oauth/token");
        var signer = new AssertionSigner(options.ClientId, tokenUrl, options.SigningKey, time);
        _tokens = new TokenProvider(_http, signer, tokenUrl, time);
        _wait = wait ?? ((delay, ct) => Task.Delay(delay, ct));
    }

    /// <summary>Who your token says you are: a quick check that everything is set up.</summary>
    public Task<Ping> PingAsync(CancellationToken ct = default) =>
        SendAsync<Ping>(HttpMethod.Get, "ping", ct: ct);

    /// <summary>Create the tenant if it is new, or update it. Safe to repeat.</summary>
    public Task<Tenant> PutTenantAsync(string tenantRef, TenantRequest tenant, CancellationToken ct = default) =>
        SendAsync<Tenant>(HttpMethod.Put, $"tenants/{Escape(tenantRef)}", tenant, ct: ct);

    public Task<Tenant> GetTenantAsync(string tenantRef, CancellationToken ct = default) =>
        SendAsync<Tenant>(HttpMethod.Get, $"tenants/{Escape(tenantRef)}", ct: ct);

    public Task<Page<Tenant>> ListTenantsAsync(string? cursor = null, CancellationToken ct = default) =>
        SendAsync<Page<Tenant>>(HttpMethod.Get, cursor is null ? "tenants" : $"tenants?cursor={Escape(cursor)}", ct: ct);

    /// <summary>The user a token acts as, in their tenant, with what they can use.</summary>
    public Task<Me> GetMeAsync(PartnerUser user, CancellationToken ct = default) =>
        SendAsync<Me>(HttpMethod.Get, $"tenants/{Escape(user.Tenant)}/me", user: user, ct: ct);

    /// <summary>
    /// Any call to the API: <paramref name="path"/> is relative to <c>/v1/</c>.
    /// Pass <paramref name="user"/> to act as one of your users; leave it out to act as your system.
    /// </summary>
    public async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null,
                                      PartnerUser? user = null, CancellationToken ct = default)
    {
        var idempotencyKey = method == HttpMethod.Post ? Guid.NewGuid().ToString() : null;
        var tokenReplaced = false;
        for (var tries = 1; ; tries++)
        {
            using var request = new HttpRequestMessage(method, new Uri(_api, path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _tokens.GetAsync(user, ct));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
            if (body is not null)
            {
                // Serialised up front, so the body goes with its Content-Length. A streamed
                // body (JsonContent, PostAsJsonAsync) goes chunked, and the API can't read that.
                request.Content = new StringContent(
                    JsonSerializer.Serialize(body, body.GetType(), Json), Encoding.UTF8, "application/json");
            }

            using var response = await _http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized && !tokenReplaced)
            {
                _tokens.Forget(user);  // revoked or expired early: get a new one, once
                tokenReplaced = true;
                tries--;
                continue;
            }
            if (response.StatusCode == HttpStatusCode.TooManyRequests && tries < MaxTries)
            {
                await _wait(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1), ct);
                continue;
            }

            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode) throw Problem(response, text);
            return JsonSerializer.Deserialize<T>(text, Json)!;
        }
    }

    private static PartnerApiException Problem(HttpResponseMessage response, string text)
    {
        string? code = null, detail = null, requestId = null;
        List<FieldError> errors = [];
        try
        {
            using var problem = JsonDocument.Parse(text);
            var json = problem.RootElement;
            code = json.TryGetProperty("code", out var c) ? c.GetString() : null;
            detail = json.TryGetProperty("detail", out var d) ? d.GetString() : null;
            requestId = json.TryGetProperty("request_id", out var r) ? r.GetString() : null;
            if (json.TryGetProperty("errors", out var fields) && fields.ValueKind == JsonValueKind.Array)
            {
                errors.AddRange(fields.EnumerateArray().Select(f => new FieldError(
                    f.TryGetProperty("pointer", out var p) ? p.GetString() ?? "" : "",
                    f.TryGetProperty("detail", out var fd) ? fd.GetString() ?? "" : "")));
            }
        }
        catch (JsonException)
        {
            // Not a problem document (a proxy's error page, say): report what we have.
        }
        return new PartnerApiException(
            (int)response.StatusCode, code ?? "unknown", detail ?? response.ReasonPhrase,
            requestId ?? RequestIds.From(response), errors);
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);
}
