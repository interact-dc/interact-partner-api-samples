# Interact Partner API: start here

This folder is your way into the Interact Partner API: every document your
team needs, and the order to use them in. The API is live on our staging
sandbox now, with everything from signing in your users to running
simulations and reading their results.

Read [How it works](how-it-works.md) first, for the shape of the integration.
Then work through [Setting up your account](setting-up.md), which says what we
do, what you do and what we need from you. The [sample client](../README.md)
shows every step in working C# code.

## The pack

| Read or use | What it is for |
| --- | --- |
| [How it works](how-it-works.md) | The integration in outline: your users, security, and how a simulation is set up, run and answered |
| [Setting up your account](setting-up.md) | The setup steps, who does each, what we need from you, and the checks to run on staging |
| [API reference](https://partners-staging.interactdc.com/developers/) | Every endpoint, field and error, in ReDoc or Swagger UI. "Try it out" works on staging |
| [OpenAPI description](https://partners-staging.interactdc.com/developers/openapi.json) | The API as OpenAPI 3.1, to generate a client from in most languages |
| [Sample client](../README.md) | Working C# (.NET 10): keys, tokens, a tenant, a user, and a whole simulation from checking a server list to reading its results |

Staging is at `https://partners-staging.interactdc.com`. You move to
`https://partners.interactdc.com` when you go live; nothing else changes.

## Your first day

1. **Make a key pair for staging** and send us its public half, as
   [Setting up your account](setting-up.md#what-we-need-from-you) describes.
   The sample does it for you:
   `dotnet run --project dotnet/samples/Walkthrough -- keygen`. Keep the
   private key to yourself.
2. **We register your system** and send you its client id. We also put
   credits in your credit pool, so you can give your test tenants some: a run
   spends them.
3. **Run the sample's walkthrough** against staging. Step by step it signs
   your system in, creates a tenant, gives it credits from your pool, signs in
   one of its users, looks up
   hardware, checks a server list and fixes it, then creates a simulation,
   runs it and prints the results. If your plan includes EU compliance, it
   then makes a compliance label for the same servers. If it runs green, your key, client id and
   network path all work.
4. **Build the same flow in your system**, using the sample's code or a
   client generated from the OpenAPI description:
    1. Your users build a server list, and you check it with
       `POST /v1/catalogue/match`.
    2. They settle each server (the suggestion, an alternative, or one from
       the catalogue pickers) or take it out. Check again as often as you
       like.
    3. Create the simulation with each server's match, then run it.

   A simulation is only created when every server is settled. That way no
   server is ever quietly left out of the results.
5. **Work through the checks** in
   [Setting up your account](setting-up.md#what-you-can-build-and-test-now).
   They cover tokens, tenants, users, errors and rate limits, so you meet each
   case on staging, not in production.

When your integration is ready, you send us a separate public key for
production, and we register it. Then you go live.

## Getting help

When a call fails, send us its request id. Every response carries one, as the
`X-Request-ID` header and as `request_id` in every error. With it we can see
exactly what happened to that call, including why a token request was refused.

Every error has a stable `code` to act on: build your handling on the code,
not on the message text. The API reference lists the codes each endpoint can
give.
