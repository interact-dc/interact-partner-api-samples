// The Interact Partner API, step by step.
//
//   dotnet run --project samples/Walkthrough -- keygen [key.pem]
//       Makes your key pair: saves the private key, prints the public JWKS to send Interact.
//
//   dotnet run --project samples/Walkthrough -- walkthrough
//       With the client id Interact gave you, goes through a whole integration:
//       checks your token, creates a tenant, signs in a user and acts as them.
//       Reads INTERACT_BASE_URL, INTERACT_CLIENT_ID and INTERACT_KEY_FILE.

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
        var tenant = await client.PutTenantAsync(tenantRef, new TenantRequest("Sample customer", ["simulations"]));
        Console.WriteLine($"   {tenant.Name}: {tenant.Status}, can use {string.Join(", ", tenant.Modules.Effective)}");

        Step(3, "List your tenants");
        var page = await client.ListTenantsAsync();
        foreach (var t in page.Items) Console.WriteLine($"   {t.TenantRef}  {t.Name}");

        Step(4, "Act as one of the customer's users, signed in by you");
        var user = new PartnerUser("sample-user-1", tenantRef, "member", Name: "Sample User");
        var me = await client.GetMeAsync(user);
        Console.WriteLine($"   {me.Subject} ({me.Role}) in {me.Tenant}: scopes {string.Join(" ", me.Scopes)}, {me.CreditBalance} credits");

        Console.WriteLine();
        Console.WriteLine("Done. Every call above is in Interact's API activity, under your client id.");
        return 0;
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

static int Usage()
{
    Console.Error.WriteLine("Usage: Walkthrough keygen [key.pem] | walkthrough");
    return 2;
}
