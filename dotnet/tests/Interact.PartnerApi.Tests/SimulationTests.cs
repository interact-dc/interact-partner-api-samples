using System.Net;
using System.Text.Json;

namespace Interact.PartnerApi.Tests;

public class CatalogueTests : ClientTestBase
{
    [Fact]
    public async Task Checking_a_list_sends_each_row_and_reads_what_it_matched()
    {
        Handler.Token().Then(HttpStatusCode.OK, """
            {"results":[
              {"ref":"a","status":"matched","confidence":1.0,"server":{"vendor":"Dell","model":"PowerEdge R640","cpu":null},
               "alternatives":[],"cpu_alternatives":[],"gpu":null},
              {"ref":"b","status":"needs_review","confidence":0.82,
               "server":{"vendor":"Dell","model":"PowerEdge R650","cpu":"Intel Xeon Gold 6338"},
               "alternatives":[{"vendor":"Dell","model":"PowerEdge R6515","cpu":null,"confidence":0.7}],
               "cpu_alternatives":[{"cpu":"Intel Xeon Gold 6330","confidence":0.6}],"gpu":null}],
             "summary":{"matched":1,"needs_review":1,"unmatched":0}}
            """);

        var check = await Client().CheckServersAsync([new("Dell", "PowerEdge R640", Ref: "a"), new("Dell", "R65O", Ref: "b")]);

        var sent = Handler.Sent[1];
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal(new Uri(BaseAddress, "v1/catalogue/match"), sent.Uri);
        var rows = JsonDocument.Parse(sent.Body).RootElement.GetProperty("rows");
        Assert.Equal("R65O", rows[1].GetProperty("model").GetString());
        Assert.Equal(1, check.Summary.NeedsReview);
        Assert.Equal("PowerEdge R650", check.Results[1].Server!.Model);
        Assert.Equal("Intel Xeon Gold 6330", check.Results[1].CpuAlternatives[0].Cpu);
    }
}

public class SimulationTests : ClientTestBase
{
    private static readonly Guid Id = Guid.Parse("6f1d3c2a-1111-4222-8333-944455556666");

    private const string SimulationJson = """
        {"id":"6f1d3c2a-1111-4222-8333-944455556666","type":"estate_refresh","name":"Leeds","visibility":"private",
         "status":"ready","servers":{"total":2,"matched":2,"needs_review":0,"unmatched":0,"excluded":0,"pending":0},
         "skipped":[]}
        """;

    [Fact]
    public async Task A_simulation_is_created_as_the_user_with_each_server_and_its_match()
    {
        Handler.Token("bob-token").Then(HttpStatusCode.Created, SimulationJson);

        var simulation = await Client().CreateSimulationAsync(Bob, new SimulationRequest("estate_refresh", "Leeds",
            Servers: [new ServerLine("rack-1", "Dell", "R64O", Quantity: 4, Match: new ServerMatch("Dell", "PowerEdge R640"))]));

        var sent = Handler.Sent[1];
        Assert.Equal(new Uri(BaseAddress, "v1/tenants/contoso/simulations"), sent.Uri);
        Assert.Equal("Bearer bob-token", sent.Headers["Authorization"]);
        var server = JsonDocument.Parse(sent.Body).RootElement.GetProperty("servers")[0];
        Assert.Equal("PowerEdge R640", server.GetProperty("match").GetProperty("model").GetString());
        Assert.False(server.TryGetProperty("gpu", out _));  // nothing sent for what isn't there
        Assert.Equal(("ready", 2), (simulation.Status, simulation.Servers.Matched));
        Assert.Equal(Id, simulation.Id);
    }

    [Fact]
    public async Task Servers_not_settled_say_which_and_what_our_matcher_found()
    {
        Handler.Token().Then(HttpStatusCode.BadRequest, """
            {"type":"x","title":"Servers not settled","status":400,"detail":"1 server is not settled.",
             "code":"servers_unresolved","request_id":"req-9",
             "errors":[{"pointer":"/servers/1","detail":"Matched as a guess."}],
             "unresolved":[{"pointer":"/servers/1","ref":"rack-2","status":"needs_review",
               "match":{"ref":"rack-2","status":"needs_review","confidence":0.8,
                        "server":{"vendor":"Dell","model":"PowerEdge R650","cpu":null},
                        "alternatives":[],"cpu_alternatives":[],"gpu":null}}]}
            """, "application/problem+json");

        var error = await Assert.ThrowsAsync<PartnerApiException>(() =>
            Client().CreateSimulationAsync(Bob, new SimulationRequest("estate_refresh", "Leeds")));

        Assert.Equal("servers_unresolved", error.Code);
        var server = Assert.Single(error.Unresolved);
        Assert.Equal(("rack-2", "needs_review", "PowerEdge R650"), (server.Ref, server.Status, server.Match!.Server!.Model));
    }
}

public class RunTests : ClientTestBase
{
    private static readonly Guid Simulation = Guid.Parse("6f1d3c2a-1111-4222-8333-944455556666");

    private const string Running = """{"version":1,"status":"running","progress":{"completed_targets":0,"total_targets":1},"resume_after":null,"credits_used":1,"error":null,"results":null}""";

    private const string Completed = """
        {"version":1,"status":"completed","progress":null,"resume_after":null,"credits_used":1,"error":null,
         "results":{"estate":{"servers":10,"lines":1,"excluded":[]},
                    "assumptions":{"server_utilisation":0.25,"server_utilisation_assumed":true,"pue":1.4,
                                   "pue_assumed":true,"currency":"GBP","energy_cost_per_kwh":"0.1500"},
                    "targets":[{"ref":"t1","vendor":"Dell","model":"PowerEdge R660","status":"completed","error":null,
                                "servers":{"current":10,"new":6},"energy_kwh_per_year":{"current":24835,"new":9934},
                                "carbon_kg_per_year":{"new":2384},
                                "reductions_pct":{"servers":40,"energy":60,"carbon":60,"operating_cost":30}}]}}
        """;

    [Fact]
    public async Task A_run_is_started_asking_to_wait_and_its_results_read()
    {
        Handler.Token().Then(HttpStatusCode.Created, Completed);

        var run = await Client().StartRunAsync(Bob, Simulation);

        var sent = Handler.Sent[1];
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.EndsWith($"/v1/tenants/contoso/simulations/{Simulation}/runs", sent.Uri.ToString());
        Assert.Equal("wait=10", sent.Headers["Prefer"]);
        Assert.True(sent.Headers.ContainsKey("Idempotency-Key"));
        Assert.True(run.Finished);
        var target = Assert.Single(run.Results!.Targets);
        Assert.Equal(("t1", 6.0, 60.0), (target.Ref, target.Servers.New, target.ReductionsPct!.Energy));
    }

    [Fact]
    public async Task A_run_still_going_is_followed_until_it_has_finished()
    {
        Handler.Token().Then(HttpStatusCode.OK, Running).Then(HttpStatusCode.OK, Running).Then(HttpStatusCode.OK, Completed);

        var run = await Client().WaitForRunAsync(Bob, Simulation, 1);

        Assert.Equal("completed", run.Status);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)], Waits);
    }

    [Fact]
    public async Task Too_few_credits_says_how_many()
    {
        Handler.Token().Then(HttpStatusCode.PaymentRequired, """
            {"type":"x","title":"Insufficient credits","status":402,"detail":"A run needs 1 credit; 0 are available.",
             "code":"insufficient_credits","request_id":"req-1","credits_needed":1,"credits_available":0}
            """, "application/problem+json");

        var error = await Assert.ThrowsAsync<PartnerApiException>(() => Client().StartRunAsync(Bob, Simulation));

        Assert.Equal((402, "insufficient_credits", 1, 0), (error.Status, error.Code, error.CreditsNeeded, error.CreditsAvailable));
    }
}
