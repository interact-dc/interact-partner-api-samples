// The reference set: one fixed input for each kind of report, and the results it should give.
//
//   dotnet run --project dotnet/samples/Walkthrough -- reference [--update] [name ...]
//
// Runs every input in reference/inputs (or only those named, e.g. 1-estate-refresh) through the API, in the tenant INTERACT_TENANT
// ("interact-reference" if unset), giving it from your pool the credits the runs need. Saves
// each run's results to reference/results/<date and time>/, and compares them with
// reference/expected/: any difference is listed by where it is in the results. With --update,
// it writes the results as the new expected ones instead (Interact does this to set the baseline).
//
// The same inputs give the same results as long as the analysis model behind the API is the
// same: if a figure differs, check the date the expected results were made against our notice
// of a model update before anything else. See reference/README.md.

using System.Text.Json;
using System.Text.Json.Nodes;
using Interact.PartnerApi;

static class ReferenceSet
{
    // Ours, not the answer: ids and clocks differ on every run.
    private static readonly HashSet<string> Ignored = ["id", "created_at", "updated_at", "completed_at", "started_at"];

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static async Task<int> Run(PartnerApiClient client, string tenantRef, bool update, IReadOnlyList<string> only)
    {
        var folder = Path.GetFullPath("reference");
        var inputs = Directory.GetFiles(Path.Combine(folder, "inputs"), "*.json").Order()
            .Where(path => only.Count == 0 || only.Contains(Path.GetFileNameWithoutExtension(path))).ToList();
        if (inputs.Count == 0)
        {
            Console.Error.WriteLine($"No inputs in {folder}/inputs. Run this from the repository's top folder.");
            return 1;
        }
        var bodies = inputs.ToDictionary(path => Path.GetFileNameWithoutExtension(path),
                                         path => JsonNode.Parse(File.ReadAllText(path))!.AsObject());

        Console.WriteLine($"Tenant {tenantRef}");
        var tenant = await TenantSetup.PutAsync(client, tenantRef, "Interact reference");
        if (!tenant.Modules.Effective.Contains("compliance"))
        {
            bodies = bodies.Where(entry => entry.Value.ContainsKey("type"))
                           .ToDictionary(entry => entry.Key, entry => entry.Value);
        }
        if (!tenant.Has("gpu_support"))
        {
            // The GPU reports need it; the others' GPU servers are taken without their GPUs,
            // so their figures won't match the expected ones.
            bodies = bodies.Where(entry => entry.Value["type"]?.GetValue<string>()?.StartsWith("gpu_") != true)
                           .ToDictionary(entry => entry.Key, entry => entry.Value);
            Console.WriteLine("   Without GPU support, the GPU reports are left out, and the GPU servers in the others " +
                              "are taken without their GPUs: expect their figures to differ.");
        }

        // A simulation costs a credit per target (Discovery one); a compliance run one.
        var needed = bodies.Values.Sum(body => body["targets"] is JsonArray targets && targets.Count > 0 ? targets.Count : 1);
        var credits = await client.GetTenantCreditsAsync(tenantRef);
        if (credits.Balance < needed)
        {
            var moved = await client.AllocateTenantCreditsAsync(tenantRef, needed - credits.Balance);
            Console.WriteLine($"   Gave it {needed - credits.Balance} credits from your pool: it has {moved.TenantBalance}.");
        }

        var user = new PartnerUser("reference-runner", tenantRef, "admin", Name: "Reference runner");
        var stamp = DateTime.UtcNow.ToString("yyyy-MM-dd-HHmm");
        var results = Directory.CreateDirectory(Path.Combine(folder, "results", stamp)).FullName;
        var failed = 0;

        foreach (var (name, body) in bodies)
        {
            Console.WriteLine($"\n{name}");
            JsonObject run;
            try
            {
                run = body.ContainsKey("type") ? await Simulation(client, user, body) : await Compliance(client, user, body);
            }
            catch (PartnerApiException error)
            {
                Console.WriteLine($"   Refused: {error.Message}");
                failed++;
                continue;
            }
            var found = new JsonObject
            {
                ["status"] = run["status"]?.DeepClone(),
                ["credits_used"] = run["credits_used"]?.DeepClone(),
                ["results"] = run["results"]?.DeepClone(),
            };
            File.WriteAllText(Path.Combine(results, $"{name}.json"), found.ToJsonString(Pretty) + "\n");
            Console.WriteLine($"   {found["status"]}, {found["credits_used"]} credit(s)");

            var expectedPath = Path.Combine(folder, "expected", $"{name}.json");
            if (update)
            {
                var expected = new JsonObject { ["made"] = DateTime.UtcNow.ToString("yyyy-MM-dd"), ["run"] = found.DeepClone() };
                File.WriteAllText(expectedPath, expected.ToJsonString(Pretty) + "\n");
                Console.WriteLine("   Saved as the expected results.");
                continue;
            }
            if (!File.Exists(expectedPath))
            {
                Console.WriteLine("   No expected results to compare with yet.");
                continue;
            }
            var reference = JsonNode.Parse(File.ReadAllText(expectedPath))!;
            var differences = new List<string>();
            Compare(reference["run"], found, "", differences);
            if (differences.Count == 0)
            {
                Console.WriteLine($"   Matches the expected results (made {reference["made"]}).");
            }
            else
            {
                failed++;
                Console.WriteLine($"   {differences.Count} difference(s) from the expected results (made {reference["made"]}):");
                foreach (var difference in differences.Take(40)) Console.WriteLine($"     {difference}");
            }
        }

        Console.WriteLine($"\nResults saved in {results}");
        return failed == 0 ? 0 : 1;
    }

    private static async Task<JsonObject> Simulation(PartnerApiClient client, PartnerUser user, JsonObject body)
    {
        var created = await client.SendAsync<JsonElement>(HttpMethod.Post, $"tenants/{user.Tenant}/simulations", body, user);
        Warn(created);
        var id = created.GetProperty("id").GetGuid();
        var run = await client.StartRunAsync(user, id);
        if (!run.Finished) run = await client.WaitForRunAsync(user, id, run.Version);
        var read = await client.SendAsync<JsonElement>(HttpMethod.Get, $"tenants/{user.Tenant}/simulations/{id}/runs/{run.Version}", user: user);
        return JsonNode.Parse(read.GetRawText())!.AsObject();
    }

    private static async Task<JsonObject> Compliance(PartnerApiClient client, PartnerUser user, JsonObject body)
    {
        var created = await client.SendAsync<JsonElement>(HttpMethod.Post, $"tenants/{user.Tenant}/compliance-reports", body, user);
        Warn(created);
        var id = created.GetProperty("id").GetGuid();
        var run = await client.StartComplianceRunAsync(user, id);
        if (!run.Finished) run = await client.WaitForComplianceRunAsync(user, id, run.Version);
        var read = await client.SendAsync<JsonElement>(HttpMethod.Get, $"tenants/{user.Tenant}/compliance-reports/{id}/runs/{run.Version}", user: user);
        return JsonNode.Parse(read.GetRawText())!.AsObject();
    }

    // What was taken but ignored, such as GPUs for a tenant without GPU support.
    private static void Warn(JsonElement created)
    {
        if (!created.TryGetProperty("warnings", out var warnings)) return;
        foreach (var warning in warnings.EnumerateArray())
        {
            Console.WriteLine($"   Ignored {warning.GetProperty("pointer").GetString()}: {warning.GetProperty("detail").GetString()}");
        }
    }

    // Every place where what was found differs from what was expected, by its path in the results.
    private static void Compare(JsonNode? expected, JsonNode? found, string path, List<string> differences)
    {
        switch (expected)
        {
            case JsonObject wanted when found is JsonObject got:
                foreach (var key in wanted.Select(p => p.Key).Union(got.Select(p => p.Key)).Where(k => !Ignored.Contains(k)))
                {
                    Compare(wanted[key], got[key], $"{path}/{key}", differences);
                }
                break;
            case JsonArray wanted when found is JsonArray got:
                if (wanted.Count != got.Count) differences.Add($"{path}: {wanted.Count} items expected, {got.Count} found");
                for (var i = 0; i < Math.Min(wanted.Count, got.Count); i++) Compare(wanted[i], got[i], $"{path}/{i}", differences);
                break;
            default:
                var a = expected?.ToJsonString() ?? "null";
                var b = found?.ToJsonString() ?? "null";
                if (a != b) differences.Add($"{path}: expected {a}, found {b}");
                break;
        }
    }
}
