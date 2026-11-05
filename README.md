# Interact Partner API samples

Working code for the [Interact Partner API](https://partners-staging.interactdc.com/developers/):
your key pair, access tokens, tenants for your customers, calls made as your
users, and a whole simulation: checking a server list, fixing it, running it
and reading the results. Copy what you need into your own system.

Everything here runs against our staging sandbox,
`https://partners-staging.interactdc.com`, until you go live on
`https://partners.interactdc.com`.

## Documentation

Start with [docs/README.md](docs/README.md): how the integration works, how
your account is set up and what we need from you, and the checks to run on
staging.

## .NET (C#)

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download). Everything
below runs from this folder.

### 1. Make your key pair

```sh
dotnet run --project dotnet/samples/Walkthrough -- keygen my-interact-key.pem
```

This saves your **private key** to `my-interact-key.pem` and prints your
**public key** as a JWKS document. Send Interact the JWKS; it holds no secret.
Keep the private key to yourself: in production, keep it in a key vault such
as Azure Key Vault, and use a separate key for staging and production.

Interact registers your public key and sends you a **client id**.

### 2. Run the walkthrough

```sh
export INTERACT_BASE_URL=https://partners-staging.interactdc.com
export INTERACT_CLIENT_ID=<your client id>
export INTERACT_KEY_FILE=my-interact-key.pem
dotnet run --project dotnet/samples/Walkthrough -- walkthrough
```

In ten steps it:

1. checks your token;
2. creates a tenant (`sample-tenant`, or set `INTERACT_TENANT`);
3. lists your tenants;
4. signs in a user of that tenant and reads who the API says they are;
5. looks up servers and CPUs in the catalogue, as your pickers would;
6. checks a customer's server list: one server named exactly, one written
   their own way, one we won't know;
7. fixes the list: takes our suggestion for the one in doubt and leaves out
   the one we don't know;
8. shows the list sent unfixed being refused, naming each server that isn't
   settled, then creates the simulation with the fixed list and a target;
9. runs it, waiting up to 10 seconds for the result (and following it if it
   takes longer);
10. prints what the target would change: servers, energy and carbon.

**A run spends one of the tenant's credits.** A new tenant has none: ask
Interact to add some to your test tenant before step 9.

### 3. Use the library in your system

`dotnet/src/Interact.PartnerApi` is a small library you can copy or reference:

```csharp
using Interact.PartnerApi;

using var key = KeyTools.LoadPrivateKeyPem(File.ReadAllText("my-interact-key.pem"));
var api = new PartnerApiClient(new PartnerApiOptions(
    new Uri("https://partners-staging.interactdc.com/"), "<your client id>", key));

// As your system: manage your tenants.
await api.PutTenantAsync("contoso", new TenantRequest("Contoso Ltd", ["simulations"]));

// As one of your users, signed in with your own sign-in (for example Microsoft Entra ID).
var user = new PartnerUser(Subject: "<their Entra object id>", Tenant: "contoso", Role: "member",
                           Name: "Bob Smith", Email: "bob@contoso.com");
var me = await api.GetMeAsync(user);

// Check the customer's list; settle each server; then create, run and read.
var check = await api.CheckServersAsync([new MatchRow("Dell", "R640", "Gold 6230R", Ref: "rack-1")]);
var suggestion = check.Results[0].Server!;  // needs_review: your user confirms it, or picks another
var simulation = await api.CreateSimulationAsync(user, new SimulationRequest("estate_refresh", "Leeds refresh",
    Servers: [new ServerLine("rack-1", "Dell", "R640", "Gold 6230R", CpuCount: 2, MemoryGb: 256, Quantity: 10,
                             Match: new ServerMatch(suggestion.Vendor, suggestion.Model, suggestion.Cpu))],
    Targets: [new TargetLine("target-1", "Dell", "PowerEdge R660", "Intel Xeon Gold 6448Y", CpuCount: 2, MemoryGb: 512)]));
var run = await api.StartRunAsync(user, simulation.Id);  // waits up to 10 seconds
if (!run.Finished) run = await api.WaitForRunAsync(user, simulation.Id, run.Version);

// Any other endpoint, by its path under /v1/.
var credits = await api.SendAsync<System.Text.Json.JsonElement>(HttpMethod.Get, "tenants/contoso/credits");
```

### How a simulation fits together

- **Check, fix, check again.** `CheckServersAsync` (`POST /v1/catalogue/match`)
  says what each server matches, and stores nothing. A server is `matched`,
  `needs_review` (a guess, with alternatives) or `unmatched`. Have your user
  settle each one (the suggestion, an alternative, or a server from the
  catalogue pickers) or take it out, and check again as often as you like.
- **Send it settled.** Create the simulation with each server and the
  `Match` it settled on (or none, for one that matched as sent). Every server
  must be settled, or nothing is created: a `PartnerApiException` with code
  `servers_unresolved` lists each one in `Unresolved`, with what we found. An
  unmatched server is never quietly left out of a run.
- **Run, and read.** A simulation is `ready` once created. Starting a run
  charges the tenant's credits (one per target) and most runs finish in a few
  seconds; `insufficient_credits` says how many are needed and available.

What it does for you:

- **Signs your requests** with your key (OAuth 2.0 `private_key_jwt`,
  RFC 7523), each one used once and valid for two minutes.
- **Gets and reuses tokens**: one for your system, one per user (token
  exchange, RFC 8693), each renewed a minute before it runs out.
- **Replaces a refused token once**, if the API rejects one.
- **Waits and retries** when you hit a rate limit, as long as the API's
  `Retry-After` says, up to three tries.
- **Adds an `Idempotency-Key`** to every POST, the same on each retry, so a
  retried request never does its work twice.
- **Waits for runs**: `StartRunAsync` asks to wait (`Prefer: wait=10`), and
  `WaitForRunAsync` follows a longer one, never sooner than a paused run says.
- **Sends request bodies with their length.** The API can't read a chunked
  body, so avoid `PostAsJsonAsync` and `JsonContent` in your own code too.
- **Raises errors you can act on**: `PartnerApiException` carries the API's
  stable `Code`, the `Detail`, any field `Errors`, servers not settled
  (`Unresolved`), credits needed and available, and the `RequestId`.

**If anything fails, send Interact the request id.** We can see exactly what
happened to that call, including why a token request was refused.

### Run the tests

```sh
dotnet test dotnet/Interact.PartnerApi.slnx
```

## More

- **API reference:** https://partners-staging.interactdc.com/developers/
  (ReDoc and Swagger UI), with the OpenAPI 3.1 description at
  https://partners-staging.interactdc.com/developers/openapi.json, which you
  can generate client code from in most languages.
- **Help:** your Interact contact, with the request id of the call in question.
