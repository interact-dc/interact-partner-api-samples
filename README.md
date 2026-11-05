# Interact Partner API samples

Working code for the [Interact Partner API](https://partners.interactdc.com/developers/):
your key pair, access tokens, tenants for your customers, and calls made as
your users. Copy what you need into your own system.

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
export INTERACT_BASE_URL=https://partners.interactdc.com
export INTERACT_CLIENT_ID=<your client id>
export INTERACT_KEY_FILE=my-interact-key.pem
dotnet run --project dotnet/samples/Walkthrough -- walkthrough
```

It checks your token, creates a tenant (`sample-tenant`, or set
`INTERACT_TENANT`), lists your tenants, then signs in a user of that tenant
and reads who the API says they are. Use the staging address Interact gives
you until you go live.

### 3. Use the library in your system

`dotnet/src/Interact.PartnerApi` is a small library you can copy or reference:

```csharp
using Interact.PartnerApi;

using var key = KeyTools.LoadPrivateKeyPem(File.ReadAllText("my-interact-key.pem"));
var api = new PartnerApiClient(new PartnerApiOptions(
    new Uri("https://partners.interactdc.com/"), "<your client id>", key));

// As your system: manage your tenants.
await api.PutTenantAsync("contoso", new TenantRequest("Contoso Ltd", ["simulations"]));

// As one of your users, signed in with your own sign-in (for example Microsoft Entra ID).
var user = new PartnerUser(Subject: "<their Entra object id>", Tenant: "contoso", Role: "member",
                           Name: "Bob Smith", Email: "bob@contoso.com");
var me = await api.GetMeAsync(user);

// Any other endpoint, by its path under /v1/.
var credits = await api.SendAsync<System.Text.Json.JsonElement>(HttpMethod.Get, "tenants/contoso/credits");
```

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
- **Sends request bodies with their length.** The API can't read a chunked
  body, so avoid `PostAsJsonAsync` and `JsonContent` in your own code too.
- **Raises errors you can act on**: `PartnerApiException` carries the API's
  stable `Code`, the `Detail`, any field `Errors`, and the `RequestId`.

**If anything fails, send Interact the request id.** We can see exactly what
happened to that call, including why a token request was refused.

### Run the tests

```sh
dotnet test dotnet/Interact.PartnerApi.slnx
```

## More

- **API reference:** https://partners.interactdc.com/developers/ (ReDoc and
  Swagger UI), with the OpenAPI 3.1 description at
  https://partners.interactdc.com/developers/openapi.json, which you can
  generate client code from in most languages.
- **Help:** your Interact contact, with the request id of the call in question.
