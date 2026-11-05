# Setting up your account

Setting up takes four stages: we create your partner account, you send us your public key, we register your system, and you build and test against our staging sandbox before going live. This page lists what each side does and what we need from you.

## Setup, step by step

Steps 1 to 8 happen on staging; steps 9 to 11 repeat the key steps for production.

| Step | Who | What happens |
| --- | --- | --- |
| 1 | Interact | We create your partner account: the modules your customers can use and how many tenants you can have, as agreed |
| 2 | You | Create a key pair for staging and keep the private key safe |
| 3 | You | Send us the items under [What we need from you](#what-we-need-from-you) |
| 4 | Interact | We register your staging system and send you its client id and the addresses you need |
| 5 | You | Check the connection: get an access token and call `GET /v1/ping` |
| 6 | You | Create a test tenant, sign in a test user and call `GET /v1/tenants/{tenant}/me` as them |
| 7 | Interact | We add credits to your test tenant |
| 8 | You | Build and test your integration against staging, starting with the checks under [What you can build and test now](#what-you-can-build-and-test-now) |
| 9 | You | Create a separate key pair for production and send us its public key |
| 10 | Interact | We register your production system and send you its client id |
| 11 | Both | Go live |

If any call fails, send us the `X-Request-ID` from the response. We can see exactly why it failed, including why a token request was refused.

## What we need from you

Never send us a private key. We only ever need the public half; anything else is refused.

| Item | What it is | Example | Needed |
| --- | --- | --- | --- |
| Technical contact | The person we work with on setup and security notices: name and email | Jane Smith, jane@yourcompany.com | Yes |
| Staging public key | The public half of your staging key pair, as the web address where you publish it (preferred) or as a JWKS document | https://auth.yourcompany.com/.well-known/jwks.json | Yes |
| Production public key | The same, for a separate production key pair | As above | Before going live |
| IP ranges | The addresses your system calls us from, if you want calls from anywhere else refused | 203.0.113.0/24 | Optional |

**Keys we accept:** ES256 (recommended), ES384, ES512, RS256 to RS512 and PS256 to PS512 with RSA keys of at least 2048 bits, and EdDSA. A key held in Azure Key Vault or a similar service works, as long as it can sign a JWT.

## What we send you

You get these for staging at step 4, and the production ones at step 10.

| Item | What it is for | Staging (live now) | Production |
| --- | --- | --- | --- |
| Client id | Names your system: the `iss` and `sub` of every signed request you send | Sent at step 4 | Sent at step 10 |
| Token address | Where your system swaps a signed request for an access token; also the `aud` of that request | https://partners-staging.interactdc.com/oauth/token | https://partners.interactdc.com/oauth/token |
| API address | Where every API call goes | https://partners-staging.interactdc.com/v1/ | https://partners.interactdc.com/v1/ |
| API reference | Every endpoint, field and error, as ReDoc or Swagger UI, with the OpenAPI description to generate code from. "Try it out" works on staging | https://partners-staging.interactdc.com/developers/ | https://partners.interactdc.com/developers/ |
| Sample client | Working C# (.NET) code: keys, tokens, a tenant, a user, and a whole simulation | [This repository](../README.md) | The same |
| How it works | The overview of the integration: users, security and simulations | [How it works](how-it-works.md) | The same |

The production addresses go live when you do. Everything on staging is reached over HTTPS with a public certificate, so your HTTP client needs no special settings.

## What you can build and test now

Everything is live on staging, simulations included, so you can build and prove your whole integration there. Work through these checks in order; each builds on the one before. The sample client's walkthrough does checks 1 to 6 and 11 to 13 for you, and its `WebhookVerifier` checks 14 and 15's signatures, so running it first shows your key and client id work.

| Check | Call | Passes when |
| --- | --- | --- |
| 1. Your system gets a token | `POST /oauth/token` with `grant_type=client_credentials` and your signed request as `client_assertion` | 200 with an `access_token` that lasts 10 minutes |
| 2. The token works | `GET /v1/ping` | 200, naming your client and its scopes |
| 3. You can create a tenant | `PUT /v1/tenants/{tenant}` with a name and `"modules": ["simulations"]` | 201 the first time, 200 when repeated |
| 4. A user can sign in | `POST /oauth/token` with `grant_type=urn:ietf:params:oauth:grant-type:token-exchange` and a signed statement about the user, then `GET /v1/tenants/{tenant}/me` with the token you get back | 200 with the user's name and role |
| 5. You can look up hardware | `GET /v1/catalogue/vendors`, `servers`, `cpus`, `gpus` and `locations` | Results come in pages; follow `next` until it is null |
| 6. You can check a server list | `POST /v1/catalogue/match` with up to 500 rows of vendor, model, CPU and GPU | Each row comes back `matched`, `needs_review` with alternatives, or `unmatched` |
| 7. You can manage users | `GET /v1/tenants/{tenant}/users`, then `DELETE` and `POST .../reactivate` on one user | A deactivated user gets no token until reactivated |
| 8. You can see a tenant's account | `GET /v1/tenants/{tenant}/credits` and `/usage` | 200 with the balance and use by month |
| 9. You can suspend and offboard | `PATCH /v1/tenants/{tenant}` with `"status": "suspended"`, then `"active"`; `DELETE` a test tenant you are finished with | A suspended tenant's users get no tokens; an offboarded tenant's reference can't be used again |
| 10. Your system handles errors | Send a request with a wrong field, an expired token and more calls than the rate limit | You act on the error's `code`, get a new token on a 401, and wait for `Retry-After` on a 429 |
| 11. A list that isn't settled is refused | `POST /v1/tenants/{tenant}/simulations` with a server that needs review or is unmatched, and no `match` | 400 `servers_unresolved`, naming each server in `unresolved`; nothing is created |
| 12. A settled list makes a simulation | The same, with each server's `match` from check 6, and a target | 201, status `ready` |
| 13. You can run it and read the results | `POST .../simulations/{id}/runs` with `Prefer: wait=10`; if it answers 202, `GET` its `Location` | The run is `completed`, with each target's servers, energy, carbon and reductions |
| 14. Your receiver gets webhooks | `PUT /v1/webhooks` with your https address, `POST /v1/webhooks/secret`, then `POST /v1/webhooks/test` | Your receiver verifies a `ping`, answers 2xx, and `GET /v1/webhooks/deliveries` shows it `delivered` |
| 15. A finished run tells you | Run a simulation as in check 13, without waiting | Your receiver gets `run.completed`, verifies it, and reads the run at its `path` |

Checks 6, 11 and 12 are how every simulation starts. Your users build the server list, you check it, and they settle each server (the suggestion, an alternative, or one from the catalogue pickers) or take it out. Then you send it with each server's match. A simulation is only created when every server is settled, so no server is ever quietly left out of the results. A run spends the tenant's credits, one per target: we add credits to your test tenant.

**The two signed statements.** Both are JWTs signed with your private key, with `kid` naming the key in their header. Each lives at most 5 minutes and is used once.

| Claim | Your system's request (checks 1 and 4) | A user's statement (check 4) |
| --- | --- | --- |
| `iss` | Your client id | Your client id |
| `sub` | Your client id | Your id for the user, which never changes (for example their Entra object id) |
| `aud` | The token address | The token address |
| `iat`, `exp`, `jti` | Issued, expiry and a unique id | The same |
| `tenant` |  | The tenant's reference, as in check 3 |
| `role` |  | `admin`, `member` or `viewer` |
| `name`, `email` |  | Optional, shown in Interact |

**Errors.** Every error is `application/problem+json` with a stable `code` to act on and a `request_id` to send us. Token errors follow OAuth: `invalid_client` means we couldn't verify your request, `invalid_grant` means we couldn't verify the user's statement.

**Webhooks.** You set them up yourself, with your system's token: the address (`PUT /v1/webhooks`) and the signing secret (`POST /v1/webhooks/secret`), which is shown to you once. We never see it. Each event is signed per the [Standard Webhooks](https://www.standardwebhooks.com/) specification, so verify it with their libraries or the sample's `WebhookVerifier`. Events are delivered at least once: de-duplicate on `webhook-id`. Answer with any 2xx within 10 seconds. Anything else is tried again for about a day, and `GET /v1/webhooks/deliveries` shows what your address answered.

**Not yet available:** EU compliance reports. We will tell you when they reach staging; nothing in checks 1 to 15 changes when they do.

## Looking after your keys

Your private key is the only thing that proves a request came from you, so treat it like a password you never share.

- **Keep it out of code and config files.** A key vault such as Azure Key Vault is the safe place.
- **Use separate keys for staging and production.** A key leaked from a test system then can't reach your customers' data.
- **Change keys without downtime.** If you publish your keys at a web address, add the new key there, switch to signing with it, then remove the old one. If you sent us the key itself, send the new one and we replace it.
- **If a key leaks, tell us at once.** We revoke that system straight away and register a replacement.
- **Changes to your IP ranges or contacts:** tell us before they take effect, so your calls aren't refused.
