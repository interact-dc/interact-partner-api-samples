namespace Interact.PartnerApi;

// The shapes of the API's requests and responses. The API reference
// (https://partners-staging.interactdc.com/developers/) describes every field; you can
// also generate these from its OpenAPI description.

/// <summary>The body of <c>PUT /v1/tenants/{tenant_ref}</c>.</summary>
public sealed record TenantRequest(string Name, IReadOnlyList<string> Modules, int? RetentionDays = null);

public sealed record TenantModules(IReadOnlyList<string> Requested, IReadOnlyList<string> Effective);

public sealed record Tenant(
    string TenantRef, string Name, string Status, string PlatformStatus, bool Active,
    TenantModules Modules, string Assurance, int RetentionDays,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>One page of a list. Pass <see cref="NextCursor"/> back as the cursor until it is null.</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor, string? Next);

public sealed record Me(
    Guid Id, string Subject, string Role, string Name, string Email,
    DateTimeOffset CreatedAt, DateTimeOffset? LastSeenAt, DateTimeOffset? DeactivatedAt,
    string Tenant, IReadOnlyList<string> Modules, IReadOnlyList<string> Scopes, int CreditBalance);

public sealed record Ping(
    string Partner, string ClientId, IReadOnlyList<string> Scopes,
    string? Tenant = null, Guid? Identity = null, string? Role = null);

// --- The hardware catalogue ---------------------------------------------------------

public sealed record ServerModel(string Vendor, string Model, int? ReleaseYear);

public sealed record Cpu(string Name, int? Cores, int? ReleaseYear);

// --- Checking a server list (POST /v1/catalogue/match) ------------------------------

/// <summary>One server as your customer's list has it.</summary>
public sealed record MatchRow(string Vendor, string Model, string? Cpu = null, string? Gpu = null, string? Ref = null);

public sealed record MatchRequest(IReadOnlyList<MatchRow> Rows);

public sealed record MatchedServer(string Vendor, string Model, string? Cpu);

public sealed record ServerAlternative(string Vendor, string Model, string? Cpu, double Confidence);

public sealed record CpuAlternative(string Cpu, double Confidence);

public sealed record GpuAlternative(string Name, double Confidence);

public sealed record GpuMatch(string Status, string? Name, double Confidence, IReadOnlyList<GpuAlternative> Alternatives);

/// <summary>
/// What a server matched. <see cref="Status"/> is <c>matched</c> (nothing to decide),
/// <c>needs_review</c> (a guess: <see cref="Server"/> is the suggestion, with alternatives) or
/// <c>unmatched</c> (not in the catalogue).
/// </summary>
public sealed record MatchResult(
    string? Ref, string Status, double Confidence, MatchedServer? Server,
    IReadOnlyList<ServerAlternative> Alternatives, IReadOnlyList<CpuAlternative> CpuAlternatives, GpuMatch? Gpu);

public sealed record MatchSummary(int Matched, int NeedsReview, int Unmatched);

public sealed record MatchResponse(IReadOnlyList<MatchResult> Results, MatchSummary Summary);

// --- Simulations ------------------------------------------------------------------

/// <summary>The match you settled on for a server: catalogue names, as the catalogue and the check give them.</summary>
public sealed record ServerMatch(string Vendor, string Model, string? Cpu = null, string? Gpu = null);

/// <summary>
/// A server in your customer's estate. Send it with its <see cref="Match"/>, or without one
/// if it matched outright when you checked the list.
/// </summary>
public sealed record ServerLine(
    string Ref, string Vendor, string Model, string? Cpu = null, int? CpuCount = null, int? MemoryGb = null,
    string? Gpu = null, int? GpuCount = null, int Quantity = 1, ServerMatch? Match = null);

/// <summary>A server to compare the estate against: catalogue entries. Host-to-Host types need a quantity.</summary>
public sealed record TargetLine(
    string Ref, string Vendor, string Model, string Cpu, int CpuCount, int MemoryGb,
    string? Gpu = null, int? GpuCount = null, int? Quantity = null);

/// <summary>The body of <c>POST /v1/tenants/{tenant}/simulations</c>. Types: <c>estate_refresh</c>, <c>host_to_host</c>, <c>discovery</c>, and the GPU variants.</summary>
public sealed record SimulationRequest(
    string Type, string Name, IReadOnlyList<ServerLine>? Servers = null, IReadOnlyList<TargetLine>? Targets = null,
    string? Visibility = null, string? CustomerName = null);

public sealed record ServerCounts(int Total, int Matched, int NeedsReview, int Unmatched, int Excluded, int Pending);

/// <summary>A simulation. <see cref="Status"/> is <c>ready</c> once its servers are settled.</summary>
public sealed record Simulation(Guid Id, string Type, string Name, string Status, ServerCounts Servers);

// --- Runs and their results ---------------------------------------------------------

public sealed record RunProgress(int CompletedTargets, int TotalTargets);

/// <summary>
/// A run. <see cref="Status"/> is <c>running</c>, <c>paused</c> (until <see cref="ResumeAfter"/>),
/// <c>completed</c>, <c>failed</c> or <c>cancelled</c>; <see cref="Results"/> once completed.
/// </summary>
public sealed record Run(
    int Version, string Status, RunProgress? Progress, DateTimeOffset? ResumeAfter, int CreditsUsed,
    string? Error, RunResults? Results)
{
    public bool Finished => Status is "completed" or "failed" or "cancelled";
}

public sealed record RunResults(Estate Estate, RunAssumptions Assumptions, IReadOnlyList<TargetResult> Targets);

/// <summary>The servers a run's figures cover, and any it left out, by your ref, and why.</summary>
public sealed record Estate(int? Servers, int? Lines, IReadOnlyList<ExcludedServer> Excluded);

public sealed record ExcludedServer(string? Ref, int Servers, string Reason);

public sealed record RunAssumptions(
    double? ServerUtilisation, bool ServerUtilisationAssumed, double? Pue, bool PueAssumed, string Currency,
    string? EnergyCostPerKwh);

public sealed record CurrentAndNew(double? Current, double? New);

public sealed record NewValue(double? New);

/// <summary>Reductions in percent: positive is less.</summary>
public sealed record Reductions(double? Servers, double? Energy, double? Carbon, double? OperatingCost);

/// <summary>One target's comparison with the estate, by your ref for it.</summary>
public sealed record TargetResult(
    string? Ref, string? Vendor, string? Model, string Status, string? Error, CurrentAndNew Servers,
    CurrentAndNew EnergyKwhPerYear, NewValue CarbonKgPerYear, Reductions? ReductionsPct);
