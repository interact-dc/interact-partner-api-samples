using System.Net;
using System.Text.Json;

namespace Interact.PartnerApi.Tests;

public abstract class ClientTestBase
{
    protected static readonly Uri BaseAddress = new("https://partners.example/");
    protected readonly FakeHandler Handler = new();
    protected readonly ManualTime Time = new(DateTimeOffset.UtcNow);
    protected readonly List<TimeSpan> Waits = [];
    protected readonly PartnerUser Bob = new("entra-oid-42", "contoso", "member");

    protected PartnerApiClient Client()
    {
        var options = new PartnerApiOptions(BaseAddress, "client-123", KeyTools.GenerateKey());
        return new PartnerApiClient(options, new HttpClient(Handler), Time, (wait, _) =>
        {
            Waits.Add(wait);
            return Task.CompletedTask;
        });
    }

    protected static Dictionary<string, string> Form(SentRequest request) =>
        request.Body.Split('&')
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(kv => Uri.UnescapeDataString(kv[0]), kv => Uri.UnescapeDataString(kv[1].Replace('+', ' ')));

    protected const string TenantJson = """
        {"tenant_ref":"contoso","name":"Contoso Ltd","status":"active","platform_status":"active","active":true,
         "modules":{"requested":["simulations"],"effective":["simulations"]},"assurance":"partner",
         "retention_days":90,"created_at":"2026-11-05T10:00:00Z","updated_at":"2026-11-05T10:00:00Z"}
        """;
}

public class TokenTests : ClientTestBase
{
    [Fact]
    public async Task A_partner_token_is_asked_for_with_a_signed_assertion()
    {
        Handler.Token().Then(HttpStatusCode.OK, """{"partner":"apaas","client_id":"client-123","scopes":[]}""");

        await Client().PingAsync();

        var tokenRequest = Handler.Sent[0];
        Assert.Equal(new Uri(BaseAddress, "oauth/token"), tokenRequest.Uri);
        var form = Form(tokenRequest);
        Assert.Equal("client_credentials", form["grant_type"]);
        Assert.Equal("urn:ietf:params:oauth:client-assertion-type:jwt-bearer", form["client_assertion_type"]);
        Assert.Equal(3, form["client_assertion"].Split('.').Length);
    }

    [Fact]
    public async Task A_partner_token_is_reused_until_shortly_before_it_runs_out()
    {
        Handler.Token("first", expiresIn: 600).Then(HttpStatusCode.OK, "{}").Then(HttpStatusCode.OK, "{}")
               .Token("second").Then(HttpStatusCode.OK, "{}");
        var client = Client();

        await client.PingAsync();
        await client.PingAsync();
        Time.Advance(TimeSpan.FromSeconds(600 - 30));
        await client.PingAsync();

        Assert.Equal(5, Handler.Sent.Count);
        Assert.Equal("Bearer first", Handler.Sent[2].Headers["Authorization"]);
        Assert.Equal("Bearer second", Handler.Sent[4].Headers["Authorization"]);
    }

    [Fact]
    public async Task A_user_token_comes_from_token_exchange_and_is_kept_per_user()
    {
        Handler.Token("bob-token").Then(HttpStatusCode.OK, "{}").Then(HttpStatusCode.OK, "{}")
               .Token("alice-token").Then(HttpStatusCode.OK, "{}");
        var client = Client();
        var alice = Bob with { Subject = "entra-oid-7" };

        await client.GetMeAsync(Bob);
        await client.GetMeAsync(Bob);
        await client.GetMeAsync(alice);

        var exchange = Form(Handler.Sent[0]);
        Assert.Equal("urn:ietf:params:oauth:grant-type:token-exchange", exchange["grant_type"]);
        Assert.Equal("urn:ietf:params:oauth:token-type:jwt", exchange["subject_token_type"]);
        Assert.Equal(new Uri(BaseAddress, "v1/tenants/contoso/me"), Handler.Sent[1].Uri);
        Assert.Equal("Bearer bob-token", Handler.Sent[2].Headers["Authorization"]);
        Assert.Equal("Bearer alice-token", Handler.Sent[4].Headers["Authorization"]);
    }

    [Fact]
    public async Task A_refused_token_request_says_why_and_how_to_trace_it()
    {
        Handler.Then(HttpStatusCode.Unauthorized,
                     """{"error":"invalid_client","error_description":"Client authentication failed."}""",
                     headers: ("X-Request-ID", "req-abc"));

        var refused = await Assert.ThrowsAsync<TokenRequestException>(() => Client().PingAsync());

        Assert.Equal("invalid_client", refused.Error);
        Assert.Equal("req-abc", refused.RequestId);
        Assert.Contains("req-abc", refused.Message);
    }
}

public class ApiCallTests : ClientTestBase
{
    [Fact]
    public async Task Calls_carry_the_token_and_ask_for_json()
    {
        Handler.Token("tok").Then(HttpStatusCode.OK, TenantJson);

        var tenant = await Client().GetTenantAsync("contoso");

        Assert.Equal("Contoso Ltd", tenant.Name);
        Assert.Equal(new[] { "simulations" }, tenant.Modules.Effective);
        var call = Handler.Sent[1];
        Assert.Equal("Bearer tok", call.Headers["Authorization"]);
        Assert.Equal("application/json", call.Headers["Accept"]);
    }

    [Fact]
    public async Task Putting_a_tenant_sends_its_name_and_modules()
    {
        Handler.Token().Then(HttpStatusCode.Created, TenantJson);

        await Client().PutTenantAsync("contoso", new TenantRequest("Contoso Ltd", ["simulations"]));

        var call = Handler.Sent[1];
        Assert.Equal(HttpMethod.Put, call.Method);
        Assert.Equal(new Uri(BaseAddress, "v1/tenants/contoso"), call.Uri);
        using var body = JsonDocument.Parse(call.Body);
        Assert.Equal("Contoso Ltd", body.RootElement.GetProperty("name").GetString());
        Assert.Equal("simulations", body.RootElement.GetProperty("modules")[0].GetString());
        Assert.False(body.RootElement.TryGetProperty("retention_days", out _), "unset fields stay out");
    }

    [Fact]
    public async Task Putting_a_tenant_sends_the_features_switched_on_and_reads_back_those_it_has()
    {
        Handler.Token().Then(HttpStatusCode.Created, TenantJson.Replace(
            "\"assurance\"", "\"flags\":{\"requested\":[\"gpu_support\"],\"effective\":[\"gpu_support\"]},\"assurance\""));

        var tenant = await Client().PutTenantAsync("contoso", new TenantRequest("Contoso Ltd", ["simulations"],
                                                                                Flags: ["gpu_support"]));

        using var body = JsonDocument.Parse(Handler.Sent[1].Body);
        Assert.Equal("gpu_support", body.RootElement.GetProperty("flags")[0].GetString());
        Assert.True(tenant.Has("gpu_support"));
    }

    [Fact]
    public async Task Features_left_unset_are_not_sent_so_the_tenant_keeps_its_own()
    {
        Handler.Token().Then(HttpStatusCode.Created, TenantJson);

        var tenant = await Client().PutTenantAsync("contoso", new TenantRequest("Contoso Ltd", ["simulations"]));

        using var body = JsonDocument.Parse(Handler.Sent[1].Body);
        Assert.False(body.RootElement.TryGetProperty("flags", out _));
        Assert.False(tenant.Has("gpu_support"), "an API without flags is read as none");
    }

    [Fact]
    public async Task A_body_is_sent_with_its_length_not_chunked()
    {
        // The API, like many servers, can't read a chunked request body: it would see an empty one.
        Handler.Token().Then(HttpStatusCode.Created, TenantJson);

        await Client().PutTenantAsync("contoso", new TenantRequest("Contoso Ltd", ["simulations"]));

        var call = Handler.Sent[1];
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(call.Body), call.ContentLength);
    }

    [Fact]
    public async Task A_tenant_reference_is_escaped_in_the_path()
    {
        Handler.Token().Then(HttpStatusCode.OK, TenantJson);

        await Client().GetTenantAsync("acme corp");

        Assert.Equal("/v1/tenants/acme%20corp", Handler.Sent[1].Uri.AbsolutePath);
    }

    [Fact]
    public async Task A_rejected_token_is_replaced_once()
    {
        Handler.Token("old").Then(HttpStatusCode.Unauthorized, Problem(401, "invalid_token"), "application/problem+json")
               .Token("new").Then(HttpStatusCode.OK, TenantJson);

        await Client().GetTenantAsync("contoso");

        Assert.Equal("Bearer new", Handler.Sent[3].Headers["Authorization"]);
    }

    [Fact]
    public async Task A_second_rejection_is_not_retried()
    {
        Handler.Token().Then(HttpStatusCode.Unauthorized, Problem(401, "invalid_token"), "application/problem+json")
               .Token().Then(HttpStatusCode.Unauthorized, Problem(401, "invalid_token"), "application/problem+json");

        var error = await Assert.ThrowsAsync<PartnerApiException>(() => Client().GetTenantAsync("contoso"));

        Assert.Equal(401, error.Status);
        Assert.Equal(4, Handler.Sent.Count);
    }

    [Fact]
    public async Task Past_a_rate_limit_it_waits_as_long_as_it_is_told_then_retries()
    {
        Handler.Token()
               .Then(HttpStatusCode.TooManyRequests, Problem(429, "rate_limited"), "application/problem+json",
                     ("Retry-After", "7"))
               .Then(HttpStatusCode.OK, TenantJson);

        await Client().GetTenantAsync("contoso");

        Assert.Equal(new[] { TimeSpan.FromSeconds(7) }, Waits);
        Assert.Equal(3, Handler.Sent.Count);
    }

    [Fact]
    public async Task It_gives_up_after_three_rate_limited_tries()
    {
        Handler.Token();
        for (var i = 0; i < 3; i++)
        {
            Handler.Then(HttpStatusCode.TooManyRequests, Problem(429, "rate_limited"), "application/problem+json",
                         ("Retry-After", "1"));
        }

        var error = await Assert.ThrowsAsync<PartnerApiException>(() => Client().GetTenantAsync("contoso"));

        Assert.Equal("rate_limited", error.Code);
        Assert.Equal(2, Waits.Count);
    }

    [Fact]
    public async Task A_post_carries_an_idempotency_key_and_a_retry_reuses_it()
    {
        Handler.Token()
               .Then(HttpStatusCode.TooManyRequests, Problem(429, "rate_limited"), "application/problem+json",
                     ("Retry-After", "1"))
               .Then(HttpStatusCode.OK, """{"id":"9f4f…","subject":"u1"}""");

        await Client().SendAsync<JsonElement>(HttpMethod.Post, "tenants/contoso/users/u1/reactivate");

        var first = Handler.Sent[1].Headers["Idempotency-Key"];
        Assert.False(string.IsNullOrEmpty(first));
        Assert.Equal(first, Handler.Sent[2].Headers["Idempotency-Key"]);
    }

    [Fact]
    public async Task An_error_says_what_went_wrong_and_how_to_trace_it()
    {
        Handler.Token().Then(HttpStatusCode.NotFound, Problem(404, "not_found", "req-404"), "application/problem+json",
                             ("X-Request-ID", "req-404"));

        var error = await Assert.ThrowsAsync<PartnerApiException>(() => Client().GetTenantAsync("nobody"));

        Assert.Equal(404, error.Status);
        Assert.Equal("not_found", error.Code);
        Assert.Equal("Nothing here.", error.Detail);
        Assert.Equal("req-404", error.RequestId);
        Assert.Contains("req-404", error.Message);
    }

    [Fact]
    public async Task Field_errors_are_listed_by_field()
    {
        Handler.Token().Then(HttpStatusCode.BadRequest, """
            {"type":"t","title":"Validation failed","status":400,"detail":"Some fields are not valid.",
             "code":"validation_failed","request_id":"req-9","errors":[{"pointer":"/name","detail":"This field is required."}]}
            """, "application/problem+json");

        var error = await Assert.ThrowsAsync<PartnerApiException>(
            () => Client().PutTenantAsync("contoso", new TenantRequest("", ["simulations"])));

        Assert.Equal(new[] { new FieldError("/name", "This field is required.") }, error.Errors);
        Assert.Contains("/name: This field is required.", error.Message);
    }

    [Fact]
    public async Task A_user_call_uses_that_users_token_not_the_partners()
    {
        Handler.Token("bob-token").Then(HttpStatusCode.OK, """
            {"id":"6f9619ff-8b86-d011-b42d-00c04fc964ff","subject":"entra-oid-42","role":"member","name":"","email":"",
             "created_at":"2026-11-05T10:00:00Z","last_seen_at":null,"deactivated_at":null,"tenant":"contoso",
             "modules":["simulations"],"scopes":["simulations:read"],"credit_balance":25}
            """);

        var me = await Client().GetMeAsync(Bob);

        Assert.Equal("entra-oid-42", me.Subject);
        Assert.Equal(25, me.CreditBalance);
        Assert.Equal("urn:ietf:params:oauth:grant-type:token-exchange", Form(Handler.Sent[0])["grant_type"]);
    }

    private static string Problem(int status, string code, string requestId = "req-1") =>
        $$"""{"type":"https://partners.example/problems/{{code}}","title":"T","status":{{status}},"detail":"Nothing here.","code":"{{code}}","request_id":"{{requestId}}"}""";
}
