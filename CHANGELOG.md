# Changelog

What changed in these samples, newest first. If you copied the C# library into your own system,
check **Breaking** first: those are the changes your code may need.

## 2026-11-10

### Breaking

- **The compliance results types are renamed.** If your code uses them, rename them as below.
  Their fields, and the JSON they read, are unchanged.

  | Was | Now |
  | --- | --- |
  | `Label` | `ComplianceResults` |
  | `LabelServer` | `ComplianceServer` |
  | `LabelLocation` | `ComplianceLocation` |

  `ComplianceRun.Results` is now a `ComplianceResults`.

### Added

- **Reference reports** in [`reference/`](reference/README.md): a fixed input for each kind of
  report, the results it gives on the staging sandbox, and what each figure covers. The
  walkthrough's `reference` command runs them in a tenant of yours and lists any figure that
  differs.
- **The servers' power profile on a compliance report**: `Facility.BiosSetting`, `efficiency` (the
  default, as simulations assume) or `performance`, where idle servers draw about twice as much.

### Changed

- **"Label" is now "compliance report"** in the guides, the code comments and the walkthrough.
  The reference compliance report's files are now `6-compliance-report.json`, so name it that
  if you run it on its own (`reference 6-compliance-report`).

## 2026-11-08

### Changed

- Wording only, in the guides and the code comments.

## 2026-11-06

### Added

- **EU compliance reports**: create one with its facility and servers, run it, and read the
  results: PUE and WUE with their grades, ITEE, ITEU and PerfCPU, energy, carbon, water and the
  servers. The walkthrough runs one when your plan and your client's scopes include compliance.
- **A server's own utilisation**: `ServerLine.Utilisation`, as a fraction (0.45 is 45%). A run's
  `RunAssumptions` say how many servers used their own figure (`ServersWithOwnUtilisation`) and
  what the rest used (`FacilityUtilisation`).
- **Your credit pool**: read it and its ledger, set when you're told it is low, give a tenant
  credits or take back what it hasn't spent (with an optional idempotency key for safe retries),
  and fund a tenant as you create it (`TenantRequest.InitialCredits`).

## 2026-11-05

### Added

- **The C# library and the walkthrough**: tokens, tenants, and a whole simulation from checking
  its servers to reading its results.
- **Webhooks**: a Standard Webhooks verifier and the settings calls.
- **The partner documentation** in [`docs/`](docs/README.md), including `credits.low`, a
  tenant's alert threshold and run limits.
