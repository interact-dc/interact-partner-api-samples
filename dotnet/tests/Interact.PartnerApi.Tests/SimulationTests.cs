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

public class ComplianceTests : ClientTestBase
{
    private static readonly Guid Id = Guid.Parse("7a2e4d3b-2222-4333-8444-a55566667777");

    [Fact]
    public async Task A_report_is_created_with_its_facility_and_each_server()
    {
        Handler.Token("bob-token").Then(HttpStatusCode.Created, """
            {"id":"7a2e4d3b-2222-4333-8444-a55566667777","name":"Dublin","visibility":"private","status":"ready",
             "servers":{"total":1,"matched":1,"needs_review":0,"unmatched":0,"excluded":0,"pending":0},
             "facility":{"country":"IE","region":null,"pue":1.32,"pue_source":"reported","wue":0.45,
                         "wue_source":"reported","networking_share":0.05,"storage_share":0.0,"server_utilisation":null,
                         "gpu_utilisation":null,"cooling":null,"carbon_intensity":null,"water_intensity":null},
             "owner":null,"created_at":"2026-11-06T10:00:00Z"}
            """);

        var report = await Client().CreateComplianceReportAsync(Bob, new ComplianceReportRequest("Dublin",
            new Facility("IE", 1.32m, 0.45m, NetworkingShare: 0.05m),
            [new ComplianceServerLine("rack-1", "Dell", "R64O", "Gold 6230", CpuCount: 2, MemoryGb: 256, Quantity: 4,
                                      SsdDrives: 2, Match: new ServerMatch("Dell", "PowerEdge R640", "Intel Xeon Gold 6230"))]));

        var sent = Handler.Sent[1];
        Assert.Equal(new Uri(BaseAddress, "v1/tenants/contoso/compliance-reports"), sent.Uri);
        var body = JsonDocument.Parse(sent.Body).RootElement;
        Assert.Equal(0.05, body.GetProperty("facility").GetProperty("networking_share").GetDouble());
        var server = body.GetProperty("servers")[0];
        Assert.Equal((2, 256, 2), (server.GetProperty("cpu_count").GetInt32(), server.GetProperty("memory_gb").GetInt32(),
                                   server.GetProperty("ssd_drives").GetInt32()));
        Assert.Equal(("ready", Id, "IE"), (report.Status, report.Id, report.Facility!.Country));
    }

    [Fact]
    public async Task A_run_waits_for_its_label_and_reads_it()
    {
        Handler.Token().Then(HttpStatusCode.Created, """
            {"version":1,"status":"completed","resume_after":null,"credits_used":1,"submitted_by":null,
             "submitted_at":"2026-11-06T10:00:00Z","completed_at":"2026-11-06T10:00:03Z","error":null,
             "results":{"location":{"country":"IE","region":null},
               "pue":{"value":1.32,"grade":"C","source":"reported"},"wue":{"value":0.45,"grade":"D","source":"reported"},
               "scheme_version":"2026-07",
               "efficiency":{"itee":{"value":11234.5,"servers_covered":3,"servers_total":4},
                             "iteu":{"value":41.2,"servers_covered":4,"servers_total":4},
                             "perf_cpu":{"value":950000.0,"servers_covered":3,"servers_total":4}},
               "energy":{"servers_kwh":7000,"it_kwh":8000,"facility_kwh":2560,"total_kwh":10560},
               "carbon":{"scope2_kgco2e":2100,"scope3_kgco2e":8000,"cue":0.347},
               "water":{"direct_m3":4,"indirect_m3":9,"total_m3":13,"methodology_version":"2026-09"},
               "grid":{"carbon_intensity_kgco2e_per_kwh":0.263,"carbon_intensity_year":2024,"water_intensity_l_per_kwh":0.822},
               "servers":[{"vendor":"Dell","model":"PowerEdge R640","cpu":"Intel Xeon Gold 6230","cpus":2,"memory_gb":256,
                           "gpu":null,"quantity":4,"utilisation":0.3,"drives":{"ssd":0,"hdd":0,"nvme":0},"grade":"B",
                           "status":"rated","avg_power_w":310.5,"annual_energy_kwh":10879.2}]}}
            """);

        var run = await Client().StartComplianceRunAsync(Bob, Id, waitSeconds: 5);

        var sent = Handler.Sent[1];
        Assert.Equal(new Uri(BaseAddress, $"v1/tenants/contoso/compliance-reports/{Id}/runs"), sent.Uri);
        Assert.Equal("wait=5", sent.Headers["Prefer"]);
        Assert.True(run.Finished);
        var label = run.Results!;
        Assert.Equal(("C", "D"), (label.Pue.Grade, label.Wue.Grade));
        Assert.Equal((3, 4), (label.Efficiency.Itee.ServersCovered, label.Efficiency.Itee.ServersTotal));
        Assert.Equal((2100L, 13L), (label.Carbon.Scope2Kgco2e, label.Water.TotalM3));
        Assert.Equal(("rated", 4), (label.Servers[0].Status, label.Servers[0].Quantity));
        Assert.Equal((0.263, 0.822), (label.Grid.CarbonIntensityKgco2ePerKwh, label.Grid.WaterIntensityLPerKwh));
        Assert.Equal(("IE", 256, 0), (label.Location.Country, label.Servers[0].MemoryGb, label.Servers[0].Drives.Ssd));
    }
}
