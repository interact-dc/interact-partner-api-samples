using System.Net;
using System.Text.Json;

namespace Interact.PartnerApi.Tests;

public class CreditTests : ClientTestBase
{
    [Fact]
    public async Task The_pool_is_read_and_its_ledger_paged()
    {
        Handler.Token()
            .Then(HttpStatusCode.OK, """{"balance":70,"alert_threshold":25}""")
            .Then(HttpStatusCode.OK, """
                {"items":[{"type":"allocation","amount":-30,"balance_after":70,"tenant":"contoso","created_at":"2026-11-06T10:00:00Z"},
                          {"type":"top_up","amount":100,"balance_after":100,"tenant":null,"created_at":"2026-11-06T09:00:00Z"}],
                 "next_cursor":null,"next":null}
                """);

        var client = Client();
        var pool = await client.GetCreditPoolAsync();
        var ledger = await client.ListCreditPoolTransactionsAsync();

        Assert.Equal((70, 25), (pool.Balance, pool.AlertThreshold));
        Assert.Equal(("allocation", -30, "contoso"), (ledger.Items[0].Type, ledger.Items[0].Amount, ledger.Items[0].Tenant));
        Assert.Null(ledger.Items[1].Tenant);
    }

    [Fact]
    public async Task Credits_are_given_with_an_idempotency_key()
    {
        Handler.Token().Then(HttpStatusCode.OK, """{"tenant":"contoso","tenant_balance":40,"pool_balance":60}""");

        var moved = await Client().AllocateTenantCreditsAsync("contoso", 40, idempotencyKey: "order-1042");

        var sent = Handler.Sent[1];
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.EndsWith("/v1/tenants/contoso/credits/allocate", sent.Uri.ToString());
        Assert.Equal(40, JsonDocument.Parse(sent.Body).RootElement.GetProperty("amount").GetInt32());
        Assert.Equal("order-1042", sent.Headers["Idempotency-Key"]);
        Assert.Equal((40, 60), (moved.TenantBalance, moved.PoolBalance));
    }

    [Fact]
    public async Task Too_few_in_the_pool_says_how_many_there_are()
    {
        Handler.Token().Then(HttpStatusCode.Conflict, """
            {"type":"https://partners.example/problems/insufficient_pool_credits","title":"Not enough credits in your pool",
             "status":409,"detail":"Your pool holds 10 credits.","code":"insufficient_pool_credits","credits_available":10}
            """, "application/problem+json");

        var refused = await Assert.ThrowsAsync<PartnerApiException>(() => Client().AllocateTenantCreditsAsync("contoso", 40));

        Assert.Equal(("insufficient_pool_credits", 10), (refused.Code, refused.CreditsAvailable));
    }

    [Fact]
    public async Task Unspent_credits_are_taken_back_and_the_threshold_set()
    {
        Handler.Token()
            .Then(HttpStatusCode.OK, """{"tenant":"contoso","tenant_balance":25,"pool_balance":75}""")
            .Then(HttpStatusCode.OK, """{"balance":75,"alert_threshold":50}""");

        var client = Client();
        var moved = await client.ReclaimTenantCreditsAsync("contoso", 15);
        var pool = await client.SetCreditPoolAlertAsync(50);

        Assert.EndsWith("/credits/reclaim", Handler.Sent[1].Uri.ToString());
        Assert.Equal(25, moved.TenantBalance);
        Assert.Equal(HttpMethod.Patch, Handler.Sent[2].Method);
        Assert.Equal(50, JsonDocument.Parse(Handler.Sent[2].Body).RootElement.GetProperty("alert_threshold").GetInt32());
        Assert.Equal(50, pool.AlertThreshold);
    }

    [Fact]
    public async Task A_tenant_can_be_funded_as_it_is_created()
    {
        Handler.Token().Then(HttpStatusCode.Created, TenantJson);

        await Client().PutTenantAsync("contoso", new TenantRequest("Contoso Ltd", ["simulations"], InitialCredits: 20));

        Assert.Equal(20, JsonDocument.Parse(Handler.Sent[1].Body).RootElement.GetProperty("initial_credits").GetInt32());
    }
}
