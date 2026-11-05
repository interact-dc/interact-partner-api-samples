namespace Interact.PartnerApi;

// The shapes of the API's requests and responses. The API reference
// (https://partners.interactdc.com/developers/) describes every field; you can
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
