// The Interact Partner API, step by step.
//
//   dotnet run --project samples/Walkthrough -- keygen [key.pem]
//       Makes your key pair: saves the private key, prints the public JWKS to send Interact.
//
//   dotnet run --project samples/Walkthrough -- walkthrough
//       With the client id Interact gave you, goes through a whole integration:
//       checks your token, creates a tenant and gives it credits from your pool,
//       signs in a user and acts as them,
//       then checks a server list, fixes it, creates a simulation, runs it and
//       reads the results. Reads INTERACT_BASE_URL, INTERACT_CLIENT_ID and
//       INTERACT_KEY_FILE. The runs spend the tenant's credits.

using Interact.PartnerApi;

return args.FirstOrDefault() switch
{
    "keygen" => KeyGen(args.ElementAtOrDefault(1) ?? "interact-partner-key.pem"),
    "walkthrough" => await Walkthrough(),
    _ => Usage(),
};

static int KeyGen(string path)
{
    if (File.Exists(path))
    {
        Console.Error.WriteLine($"{path} already exists. Choose another file name rather than lose that key.");
        return 1;
    }
    using var key = KeyTools.GenerateKey();
    File.WriteAllText(path, KeyTools.ExportPrivateKeyPem(key));
    if (!OperatingSystem.IsWindows())
    {
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
    Console.WriteLine($"Private key saved to {path}. Keep it secret: in production, keep it in a key vault.");
    Console.WriteLine();
    Console.WriteLine("Send Interact this public key (it holds no secret):");
    Console.WriteLine(KeyTools.PublicJwks(key));
    return 0;
}

static async Task<int> Walkthrough()
{
    var baseUrl = Setting("INTERACT_BASE_URL");
    var clientId = Setting("INTERACT_CLIENT_ID");
    var keyFile = Setting("INTERACT_KEY_FILE");
    if (baseUrl is null || clientId is null || keyFile is null) return 1;

    using var key = KeyTools.LoadPrivateKeyPem(File.ReadAllText(keyFile));
    var client = new PartnerApiClient(new PartnerApiOptions(new Uri(baseUrl.TrimEnd('/') + "/"), clientId, key));
    var tenantRef = Environment.GetEnvironmentVariable("INTERACT_TENANT") ?? "sample-tenant";

    try
    {
        Step(1, "Check your system's token");
        var ping = await client.PingAsync();
        Console.WriteLine($"   Signed in as client {ping.ClientId} of partner {ping.Partner}, scopes: {string.Join(" ", ping.Scopes)}");

        Step(2, $"Create (or update) a tenant for one of your customers: {tenantRef}");
        Tenant tenant;
        try
        {
            tenant = await client.PutTenantAsync(tenantRef, new TenantRequest("Sample customer", ["simulations", "compliance"]));
        }
        catch (PartnerApiException refused) when (refused.Code == "validation_failed")
        {
            // Your plan doesn't include EU compliance: the walkthrough leaves those steps out.
            tenant = await client.PutTenantAsync(tenantRef, new TenantRequest("Sample customer", ["simulations"]));
        }
        Console.WriteLine($"   {tenant.Name}: {tenant.Status}, can use {string.Join(", ", tenant.Modules.Effective)}");

        Step(3, "Give the tenant credits from your pool, as many as it is short of");
        // The run below costs one credit, and the compliance run one more.
        const int needed = 2;
        var credits = await client.GetTenantCreditsAsync(tenantRef);
        var pool = await client.GetCreditPoolAsync();
        if (credits.Balance >= needed)
        {
            Console.WriteLine($"   It has {credits.Balance} credits already. Your pool holds {pool.Balance}.");
        }
        else if (pool.Balance >= needed - credits.Balance)
        {
            var moved = await client.AllocateTenantCreditsAsync(tenantRef, needed - credits.Balance);
            Console.WriteLine($"   Gave {needed - credits.Balance}: it has {moved.TenantBalance}, your pool {moved.PoolBalance}.");
        }
        else
        {
            Console.Error.WriteLine($"   Your pool holds {pool.Balance} credits: ask Interact to top it up.");
            return 1;
        }

        Step(4, "List your tenants");
        var page = await client.ListTenantsAsync();
        foreach (var t in page.Items) Console.WriteLine($"   {t.TenantRef}  {t.Name}");

        Step(5, "Act as one of the customer's users, signed in by you");
        var user = new PartnerUser("sample-user-1", tenantRef, "member", Name: "Sample User");
        var me = await client.GetMeAsync(user);
        Console.WriteLine($"   {me.Subject} ({me.Role}) in {me.Tenant}: scopes {string.Join(" ", me.Scopes)}, {me.CreditBalance} credits");

        Step(6, "Look up hardware in the catalogue, as your pickers would");
        // An estate of PowerEdge R640s, compared against the R660 with a newer CPU.
        var source = await Find(client.SearchServersAsync("PowerEdge R640"), s => s.Model, "PowerEdge R640");
        var target = await Find(client.SearchServersAsync("PowerEdge R660"), s => s.Model, "PowerEdge R660");
        var sourceCpu = await Find(client.SearchCpusAsync("Gold 6230R"), c => c.Name, "Intel Xeon Gold 6230R");
        var targetCpu = await Find(client.SearchCpusAsync("Gold 6448Y"), c => c.Name, "Intel Xeon Gold 6448Y");
        if (source is null || target is null || sourceCpu is null || targetCpu is null)
        {
            Console.Error.WriteLine("   The catalogue doesn't have the hardware this walkthrough uses.");
            return 1;
        }
        Console.WriteLine($"   Estate: {source.Vendor} {source.Model} with {sourceCpu.Name}");
        Console.WriteLine($"   Target: {target.Vendor} {target.Model} with {targetCpu.Name}");

        Step(7, "Check your customer's server list");
        // As a customer's list has it: one server named exactly, one written their own way,
        // one we won't know.
        List<ServerLine> estate =
        [
            new("rack-1", source.Vendor, source.Model, sourceCpu.Name, CpuCount: 2, MemoryGb: 256, Quantity: 10),
            new("rack-2", source.Vendor.ToLowerInvariant(), Abbreviate(source.Model), "Gold 6230R", CpuCount: 2,
                MemoryGb: 512, Quantity: 4),
            new("rack-3", "Acme", "Toaster 3000", CpuCount: 1, MemoryGb: 64, Quantity: 2),
        ];
        var check = await client.CheckServersAsync(estate.Select(s => new MatchRow(s.Vendor, s.Model, s.Cpu, s.Gpu, s.Ref)).ToList());
        foreach (var result in check.Results) Console.WriteLine($"   {result.Ref}: {Describe(result)}");

        Step(8, "Fix the list: settle each server, or take it out");
        // Your user would choose here: the suggestion, an alternative, or a server from the
        // catalogue pickers. This takes the suggestion, and leaves out what we don't know.
        List<ServerLine> settled = [];
        foreach (var (server, result) in estate.Zip(check.Results))
        {
            switch (result.Status)
            {
                case "matched":
                    settled.Add(server);
                    Console.WriteLine($"   {server.Ref}: kept, matched as sent");
                    break;
                case "needs_review":
                    var chosen = result.Server!;
                    settled.Add(server with { Match = new ServerMatch(chosen.Vendor, chosen.Model, chosen.Cpu ?? server.Cpu) });
                    Console.WriteLine($"   {server.Ref}: settled as {chosen.Vendor} {chosen.Model}");
                    break;
                default:
                    Console.WriteLine($"   {server.Ref}: taken out, not in the catalogue");
                    break;
            }
        }

        Step(9, "Create the simulation, with a target to compare against");
        var targets = new[] { new TargetLine("target-1", target.Vendor, target.Model, targetCpu.Name, CpuCount: 2, MemoryGb: 512) };
        try
        {
            // Sent as it was, unfixed: refused, naming each server that isn't settled.
            await client.CreateSimulationAsync(user, new SimulationRequest("estate_refresh", "Unfixed list", estate, targets));
        }
        catch (PartnerApiException refused) when (refused.Code == "servers_unresolved")
        {
            Console.WriteLine("   The list as it came was refused, and nothing was created:");
            foreach (var server in refused.Unresolved) Console.WriteLine($"     {server.Ref}: {server.Status}");
        }
        var simulation = await client.CreateSimulationAsync(user, new SimulationRequest(
            "estate_refresh", "Sample estate refresh", settled, targets));
        Console.WriteLine($"   Created {simulation.Id}: {simulation.Status}, {simulation.Servers.Matched} servers matched");

        Step(10, "Run it, waiting for the result");
        var run = await client.StartRunAsync(user, simulation.Id, waitSeconds: 10);
        if (!run.Finished)
        {
            Console.WriteLine($"   Still {run.Status} after 10 seconds; following it...");
            run = await client.WaitForRunAsync(user, simulation.Id, run.Version);
        }
        Console.WriteLine($"   Run {run.Version}: {run.Status}, {run.CreditsUsed} credit(s)");
        if (run.Results is null)
        {
            Console.Error.WriteLine($"   No results: {run.Error}");
            return 1;
        }

        Step(11, "Read the results");
        var results = run.Results;
        Console.WriteLine($"   The estate: {results.Estate.Servers} servers");
        foreach (var excluded in results.Estate.Excluded) Console.WriteLine($"   Left out: {excluded.Ref} ({excluded.Reason})");
        foreach (var t in results.Targets)
        {
            if (t.Status != "completed")
            {
                Console.WriteLine($"   {t.Ref}: {t.Status}, {t.Error}");
                continue;
            }
            Console.WriteLine($"   {t.Ref} ({t.Vendor} {t.Model}): {t.Servers.Current} servers become {t.Servers.New}");
            Console.WriteLine($"     energy {t.EnergyKwhPerYear.Current:N0} -> {t.EnergyKwhPerYear.New:N0} kWh a year, " +
                              $"{t.ReductionsPct?.Energy}% less; carbon {t.CarbonKgPerYear.New:N0} kg a year, " +
                              $"{t.ReductionsPct?.Carbon}% less");
        }

        // The tenant must have the module, and your client the scope its users' tokens carry.
        if (tenant.Modules.Effective.Contains("compliance") && me.Scopes.Contains("compliance:write"))
        {
            Step(12, "Create an EU compliance report for the data centre those servers are in");
            // The same settled servers, with the facility's own figures. Shares are fractions.
            var dataCentre = new Facility("IE", Pue: 1.32m, Wue: 0.45m, NetworkingShare: 0.05m, StorageShare: 0.10m,
                                          ServerUtilisation: 0.30m);
            // A compliance server needs its CPU count and memory: never guess them.
            var servers = settled.Select(s => new ComplianceServerLine(
                s.Ref, s.Vendor, s.Model, s.Cpu,
                s.CpuCount ?? throw new InvalidOperationException($"{s.Ref} has no CPU count"),
                s.MemoryGb ?? throw new InvalidOperationException($"{s.Ref} has no memory"),
                Quantity: s.Quantity, SsdDrives: 2, Match: s.Match)).ToList();
            var report = await client.CreateComplianceReportAsync(user, new ComplianceReportRequest(
                "Sample data centre label", dataCentre, servers));
            Console.WriteLine($"   Created {report.Id}: {report.Status}, {report.Servers.Matched} servers matched");

            Step(13, "Run it, waiting for the label");
            var labelRun = await client.StartComplianceRunAsync(user, report.Id, waitSeconds: 10);
            if (!labelRun.Finished)
            {
                Console.WriteLine($"   Still {labelRun.Status} after 10 seconds; following it...");
                labelRun = await client.WaitForComplianceRunAsync(user, report.Id, labelRun.Version);
            }
            Console.WriteLine($"   Run {labelRun.Version}: {labelRun.Status}, {labelRun.CreditsUsed} credit(s)");

            Step(14, "Read the label");
            if (labelRun.Results is not { } label)
            {
                Console.Error.WriteLine($"   No label: {labelRun.Error}");
                return 1;
            }
            Console.WriteLine($"   PUE {label.Pue.Value} (grade {label.Pue.Grade}), WUE {label.Wue.Value} (grade {label.Wue.Grade}), " +
                              $"scheme {label.SchemeVersion}");
            var itee = label.Efficiency.Itee;
            Console.WriteLine($"   ITEE {itee.Value:N0}, over {itee.ServersCovered} of {itee.ServersTotal} servers");
            Console.WriteLine($"   A year: {label.Energy.TotalKwh:N0} kWh, {label.Carbon.Scope2Kgco2e:N0} kg CO2e (scope 2), " +
                              $"{label.Water.TotalM3:N0} m3 of water");
            foreach (var server in label.Servers)
                Console.WriteLine($"   {server.Quantity} x {server.Vendor} {server.Model}: {server.Status}, grade {server.Grade}");
        }
        else
        {
            Console.WriteLine("\n(EU compliance isn't in your plan or your client's scopes, so its steps are left out.)");
        }

        Console.WriteLine();
        Console.WriteLine("Done. Every call above is in Interact's API activity, under your client id.");
        return 0;
    }
    catch (PartnerApiException error) when (error.Code == "insufficient_credits")
    {
        Console.Error.WriteLine($"The tenant has {error.CreditsAvailable} credits and a run needs {error.CreditsNeeded}. " +
                                "Ask Interact to add some to the tenant, then run this again.");
        return 1;
    }
    catch (PartnerApiException error)
    {
        Console.Error.WriteLine($"The API refused a call: {error.Message}");
        return 1;
    }
    catch (TokenRequestException error)
    {
        Console.Error.WriteLine($"{error.Message}. Quote the request id to Interact to find out why.");
        return 1;
    }
}

static string? Setting(string name)
{
    var value = Environment.GetEnvironmentVariable(name);
    if (string.IsNullOrWhiteSpace(value)) Console.Error.WriteLine($"Set {name} first.");
    return value;
}

static void Step(int number, string what) => Console.WriteLine($"\n{number}. {what}");

// The first of a search's results named exactly `name`, or null.
static async Task<T?> Find<T>(Task<Page<T>> search, Func<T, string> nameOf, string name) where T : class =>
    (await search).Items.FirstOrDefault(item => string.Equals(nameOf(item), name, StringComparison.OrdinalIgnoreCase));

// "PowerEdge R640" as a customer might write it: "R640".
static string Abbreviate(string model) => model.Contains(' ') ? model[(model.LastIndexOf(' ') + 1)..] : model + "x";

static string Describe(MatchResult result) => result.Status switch
{
    "matched" => $"matched {result.Server!.Vendor} {result.Server.Model}",
    "needs_review" => $"needs review: perhaps {result.Server!.Vendor} {result.Server.Model}" +
                      (result.Alternatives.Count > 0 ? $", or {string.Join(", ", result.Alternatives.Take(2).Select(a => a.Model))}" : ""),
    _ => "unmatched: not in the catalogue",
};

static int Usage()
{
    Console.Error.WriteLine("Usage: Walkthrough keygen [key.pem] | walkthrough");
    return 2;
}
