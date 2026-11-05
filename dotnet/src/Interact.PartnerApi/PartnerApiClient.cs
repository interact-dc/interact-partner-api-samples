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

    /// <summary>Server models in the catalogue, by words in their vendor or model.</summary>
    public Task<Page<ServerModel>> SearchServersAsync(string words, CancellationToken ct = default) =>
        SendAsync<Page<ServerModel>>(HttpMethod.Get, $"catalogue/servers?q={Escape(words)}", ct: ct);

    public Task<Page<Cpu>> SearchCpusAsync(string words, CancellationToken ct = default) =>
        SendAsync<Page<Cpu>>(HttpMethod.Get, $"catalogue/cpus?q={Escape(words)}", ct: ct);

    /// <summary>
    /// Check a server list against the catalogue: what each server matches, and what needs a
    /// decision. Stores nothing: check, fix, and check again as often as you like.
    /// </summary>
    public Task<MatchResponse> CheckServersAsync(IReadOnlyList<MatchRow> rows, CancellationToken ct = default) =>
        SendAsync<MatchResponse>(HttpMethod.Post, "catalogue/match", new MatchRequest(rows), ct: ct);

    /// <summary>
    /// Create a simulation of the user's. Every server must be settled, or nothing is created and a
    /// <see cref="PartnerApiException"/> with code <c>servers_unresolved</c> names each one
    /// (<see cref="PartnerApiException.Unresolved"/>).
    /// </summary>
    public Task<Simulation> CreateSimulationAsync(PartnerUser user, SimulationRequest simulation,
                                                  CancellationToken ct = default) =>
        SendAsync<Simulation>(HttpMethod.Post, $"tenants/{Escape(user.Tenant)}/simulations", simulation, user, ct);

    /// <summary>
    /// Start a run, charging the tenant's credits, and wait up to <paramref name="waitSeconds"/>
    /// (at most 10) for it to finish. A run still going when the wait ends comes back unfinished:
    /// follow it with <see cref="WaitForRunAsync"/>.
    /// </summary>
    public Task<Run> StartRunAsync(PartnerUser user, Guid simulationId, int waitSeconds = 10,
                                   CancellationToken ct = default) =>
        SendAsync<Run>(HttpMethod.Post, $"tenants/{Escape(user.Tenant)}/simulations/{simulationId}/runs", user: user,
                       headers: waitSeconds > 0 ? new() { ["Prefer"] = $"wait={waitSeconds}" } : null, ct: ct);

    public Task<Run> GetRunAsync(PartnerUser user, Guid simulationId, int version, CancellationToken ct = default) =>
        SendAsync<Run>(HttpMethod.Get, $"tenants/{Escape(user.Tenant)}/simulations/{simulationId}/runs/{version}",
                       user: user, ct: ct);

    /// <summary>Read the run every <paramref name="every"/> (a second by default) until it has finished.</summary>
    public async Task<Run> WaitForRunAsync(PartnerUser user, Guid simulationId, int version, TimeSpan? every = null,
                                           CancellationToken ct = default)
    {
        while (true)
        {
            var run = await GetRunAsync(user, simulationId, version, ct);
            if (run.Finished) return run;
            // A paused run waits for capacity at the analysis service: no sooner than it says.
            var delay = every ?? TimeSpan.FromSeconds(1);
            if (run.ResumeAfter is { } resume && resume - DateTimeOffset.UtcNow > delay) delay = resume - DateTimeOffset.UtcNow;
            await _wait(delay, ct);
        }
    }

    /// <summary>
    /// Any call to the API: <paramref name="path"/> is relative to <c>/v1/</c>.
    /// Pass <paramref name="user"/> to act as one of your users; leave it out to act as your system.
    /// </summary>
    public async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null,
                                      PartnerUser? user = null, CancellationToken ct = default,
                                      Dictionary<string, string>? headers = null)
    {
        var idempotencyKey = method == HttpMethod.Post ? Guid.NewGuid().ToString() : null;
        var tokenReplaced = false;
        for (var tries = 1; ; tries++)
        {
            using var request = new HttpRequestMessage(method, new Uri(_api, path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _tokens.GetAsync(user, ct));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
            foreach (var (name, value) in headers ?? []) request.Headers.TryAddWithoutValidation(name, value);
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
        int? needed = null, available = null;
        List<FieldError> errors = [];
        List<UnresolvedServer> unresolved = [];
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
            if (json.TryGetProperty("unresolved", out var servers) && servers.ValueKind == JsonValueKind.Array)
            {
                unresolved.AddRange(servers.EnumerateArray().Select(s => new UnresolvedServer(
                    s.GetProperty("pointer").GetString() ?? "",
                    s.TryGetProperty("ref", out var r2) ? r2.GetString() : null,
                    s.GetProperty("status").GetString() ?? "",
                    s.TryGetProperty("match", out var m) && m.ValueKind == JsonValueKind.Object
                        ? m.Deserialize<MatchResult>(Json) : null)));
            }
            if (json.TryGetProperty("credits_needed", out var n)) needed = n.GetInt32();
            if (json.TryGetProperty("credits_available", out var a)) available = a.GetInt32();
        }
        catch (JsonException)
        {
            // Not a problem document (a proxy's error page, say): report what we have.
        }
        return new PartnerApiException(
            (int)response.StatusCode, code ?? "unknown", detail ?? response.ReasonPhrase,
            requestId ?? RequestIds.From(response), errors, unresolved, needed, available);
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);
}
