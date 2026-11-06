using System.Text.Json.Serialization;

namespace Interact.PartnerApi;

// The shapes of the API's requests and responses. The API reference
// (https://partners-staging.interactdc.com/developers/) describes every field; you can
// also generate these from its OpenAPI description.

/// <summary>
/// The body of <c>PUT /v1/tenants/{tenant_ref}</c>. <see cref="InitialCredits"/> come from your pool
/// when the tenant is created; they are ignored when it already exists.
/// </summary>
public sealed record TenantRequest(string Name, IReadOnlyList<string> Modules, int? RetentionDays = null,
                                   int? InitialCredits = null);

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
/// if it matched outright when you checked the list. <see cref="Utilisation"/> is its own measured
/// average utilisation, as a fraction (0.45 is 45%); without one it uses the simulation's.
/// </summary>
public sealed record ServerLine(
    string Ref, string Vendor, string Model, string? Cpu = null, int? CpuCount = null, int? MemoryGb = null,
    string? Gpu = null, int? GpuCount = null, int Quantity = 1, ServerMatch? Match = null,
    decimal? Utilisation = null);

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

/// <summary>
/// What a run's figures assumed. Where some servers had their own utilisation,
/// <see cref="ServersWithOwnUtilisation"/> says how many, the rest used <see cref="FacilityUtilisation"/>,
/// and <see cref="ServerUtilisation"/> is their capacity-weighted utilisation, the workload each target was evaluated against.
/// </summary>
public sealed record RunAssumptions(
    double? ServerUtilisation, bool ServerUtilisationAssumed, double? Pue, bool PueAssumed, string Currency,
    string? EnergyCostPerKwh, double? FacilityUtilisation = null, int ServersWithOwnUtilisation = 0);

public sealed record CurrentAndNew(double? Current, double? New);

public sealed record NewValue(double? New);

/// <summary>Reductions in percent: positive is less.</summary>
public sealed record Reductions(double? Servers, double? Energy, double? Carbon, double? OperatingCost);

/// <summary>One target's comparison with the estate, by your ref for it.</summary>
public sealed record TargetResult(
    string? Ref, string? Vendor, string? Model, string Status, string? Error, CurrentAndNew Servers,
    CurrentAndNew EnergyKwhPerYear, NewValue CarbonKgPerYear, Reductions? ReductionsPct);

// --- Webhooks ------------------------------------------------------------------------

public sealed record WebhookAddress(string Url);

public sealed record WebhookSettings(string? Url, bool HasSecret, IReadOnlyList<string> Events);

public sealed record WebhookSecret(string Secret);

public sealed record WebhookTest(string EventId);

/// <summary>One event to your address. <see cref="Status"/> is <c>pending</c>, <c>delivered</c> or <c>failed</c>.</summary>
public sealed record WebhookDelivery(
    string EventId, string Type, string Status, int Attempts, DateTimeOffset? NextAttemptAt, int? LastStatusCode,
    string? LastError, DateTimeOffset CreatedAt, DateTimeOffset? DeliveredAt);

// --- EU compliance reports ------------------------------------------------------------

/// <summary>
/// The data centre a compliance report describes. Shares and utilisation are fractions
/// (0.05 is 5%), to 3 decimal places; PUE and WUE to 2. Decimals, so a figure arrives exactly as
/// written. Set or replaced as a whole. Countries: <c>GET /v1/catalogue/locations</c>.
/// </summary>
public sealed record Facility(
    string Country, decimal Pue, decimal Wue, string? Region = null, string? PueSource = null,
    string? WueSource = null, decimal? NetworkingShare = null, decimal? StorageShare = null,
    decimal? ServerUtilisation = null, decimal? GpuUtilisation = null, string? Cooling = null,
    decimal? CarbonIntensity = null, decimal? WaterIntensity = null);

/// <summary>
/// A server in the data centre: a simulation's, with what a label also needs. Its CPU count
/// and memory are required: without them it would add nothing to the label.
/// </summary>
public sealed record ComplianceServerLine(
    string Ref, string Vendor, string Model, string? Cpu, int CpuCount, int MemoryGb, string? Gpu = null,
    int? GpuCount = null, int Quantity = 1, int SsdDrives = 0, int HddDrives = 0, int NvmeDrives = 0,
    decimal? Utilisation = null, decimal? GpuUtilisation = null, ServerMatch? Match = null);

/// <summary>The body of <c>POST /v1/tenants/{tenant}/compliance-reports</c>.</summary>
public sealed record ComplianceReportRequest(
    string Name, Facility? Facility = null, IReadOnlyList<ComplianceServerLine>? Servers = null,
    string? Visibility = null, string? CustomerName = null, string? CustomerReference = null, string? Notes = null);

/// <summary>A compliance report. <see cref="Status"/> is <c>ready</c> once it has servers, settled, and its facility.</summary>
public sealed record ComplianceReport(Guid Id, string Name, string Status, ServerCounts Servers, Facility? Facility);

/// <summary>
/// A compliance run. <see cref="Status"/> is <c>running</c>, <c>paused</c> (until
/// <see cref="ResumeAfter"/>), <c>completed</c> or <c>failed</c>; <see cref="Results"/> once completed.
/// </summary>
public sealed record ComplianceRun(
    int Version, string Status, DateTimeOffset? ResumeAfter, int CreditsUsed, string? Error, Label? Results)
{
    public bool Finished => Status is "completed" or "failed";
}

/// <summary>A figure the label grades, A to G.</summary>
public sealed record Graded(double? Value, string? Grade, string? Source);

/// <summary>A fleet figure, and how many of the servers it was worked out over.</summary>
public sealed record Covered(double? Value, int? ServersCovered, int? ServersTotal);

public sealed record Efficiency(Covered Itee, Covered Iteu, Covered PerfCpu);

/// <summary>A year's energy.</summary>
public sealed record Energy(long? ServersKwh, long? ItKwh, long? FacilityKwh, long? TotalKwh);

/// <summary>A year's carbon.</summary>
public sealed record Carbon(
    [property: JsonPropertyName("scope2_kgco2e")] long? Scope2Kgco2e,
    [property: JsonPropertyName("scope3_kgco2e")] long? Scope3Kgco2e,
    double? Cue);

/// <summary>A year's water. Compare volumes only between labels of the same methodology version.</summary>
public sealed record Water(
    [property: JsonPropertyName("direct_m3")] long? DirectM3,
    [property: JsonPropertyName("indirect_m3")] long? IndirectM3,
    [property: JsonPropertyName("total_m3")] long? TotalM3,
    string? MethodologyVersion);

public sealed record Drives(int Ssd, int Hdd, int Nvme);

/// <summary>Servers alike in everything here, together. <see cref="Status"/>: <c>rated</c>, <c>server_not_found</c>, <c>cpu_not_found</c> or <c>not_reported</c>.</summary>
public sealed record LabelServer(
    string? Vendor, string? Model, string? Cpu, int? Cpus, int? MemoryGb, string? Gpu, int Quantity, double? Utilisation,
    Drives Drives, string? Grade, string Status, double? AvgPowerW, double? AnnualEnergyKwh);

public sealed record LabelLocation(string? Country, string? Region);

/// <summary>The location's grid figures the label used.</summary>
public sealed record Grid(
    [property: JsonPropertyName("carbon_intensity_kgco2e_per_kwh")] double? CarbonIntensityKgco2ePerKwh,
    int? CarbonIntensityYear,
    [property: JsonPropertyName("water_intensity_l_per_kwh")] double? WaterIntensityLPerKwh);

/// <summary>A completed run's EU data centre label.</summary>
public sealed record Label(
    LabelLocation Location, Graded Pue, Graded Wue, string? SchemeVersion, Efficiency Efficiency, Energy Energy,
    Carbon Carbon, Water Water, Grid Grid, IReadOnlyList<LabelServer> Servers);

// --- Credits ----------------------------------------------------------------------

/// <summary>
/// Your credit pool: credits you have bought from Interact, to give your tenants. At or below
/// <see cref="AlertThreshold"/> you are sent <c>credits.pool_low</c> and an email; 0 never.
/// </summary>
public sealed record CreditPool(int Balance, int AlertThreshold);

/// <summary>
/// One movement of your pool. <see cref="Type"/>: <c>top_up</c>, <c>adjustment</c>, <c>allocation</c>
/// (given to <see cref="Tenant"/>), <c>reclaim</c> (taken back from it) or <c>offboard_return</c>.
/// </summary>
public sealed record PoolTransaction(string Type, int Amount, int BalanceAfter, string? Tenant, DateTimeOffset CreatedAt);

/// <summary>Where credits stand after a move between your pool and a tenant.</summary>
public sealed record CreditsMoved(string Tenant, int TenantBalance, int PoolBalance);

public sealed record TenantCreditTransaction(string Type, int Amount, int BalanceAfter, DateTimeOffset CreatedAt);

/// <summary>A tenant's credits: what its runs spend.</summary>
public sealed record TenantCredits(int Balance, int AlertThreshold, IReadOnlyList<TenantCreditTransaction> Transactions);

internal sealed record CreditAmount(int Amount);

internal sealed record PoolThreshold(int AlertThreshold);
