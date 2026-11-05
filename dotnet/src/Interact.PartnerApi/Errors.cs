namespace Interact.PartnerApi;

/// <summary>
/// An error from the API (RFC 9457 problem details). Act on <see cref="Code"/>,
/// which is stable. Quote <see cref="RequestId"/> to Interact and we can see
/// exactly what happened to that call.
/// </summary>
public sealed class PartnerApiException(int status, string code, string? detail, string? requestId,
                                         IReadOnlyList<FieldError>? errors = null,
                                         IReadOnlyList<UnresolvedServer>? unresolved = null,
                                         int? creditsNeeded = null, int? creditsAvailable = null)
    : Exception($"{status} {code}: {detail}{Describe(errors)} (request id {requestId ?? "unknown"})")
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public string? Detail { get; } = detail;
    public string? RequestId { get; } = requestId;

    /// <summary>For <c>validation_failed</c>: what was wrong with each field, by JSON Pointer into your request body.</summary>
    public IReadOnlyList<FieldError> Errors { get; } = errors ?? [];

    /// <summary>For <c>servers_unresolved</c>: each server that isn't settled, and what our matcher found for it.</summary>
    public IReadOnlyList<UnresolvedServer> Unresolved { get; } = unresolved ?? [];

    /// <summary>For <c>insufficient_credits</c>: what a run needs, and what the tenant has.</summary>
    public int? CreditsNeeded { get; } = creditsNeeded;
    public int? CreditsAvailable { get; } = creditsAvailable;

    private static string Describe(IReadOnlyList<FieldError>? errors) =>
        errors is { Count: > 0 } ? " " + string.Join("; ", errors.Select(e => $"{e.Pointer}: {e.Detail}")) : "";
}

/// <summary>One field's problem: <see cref="Pointer"/> is a JSON Pointer such as <c>/name</c>.</summary>
public sealed record FieldError(string Pointer, string Detail);

/// <summary>
/// A server that wasn't settled: <see cref="Status"/> is <c>needs_review</c> or <c>unmatched</c>
/// (<see cref="Match"/> is what our matcher found, as the check gives it) or <c>match_refused</c>
/// (the match you sent isn't ours: see the errors under <see cref="Pointer"/>).
/// </summary>
public sealed record UnresolvedServer(string Pointer, string? Ref, string Status, MatchResult? Match);

/// <summary>
/// The token endpoint refused a request (RFC 6749). The reason is deliberately
/// general; ask Interact, quoting <see cref="RequestId"/>, for the details.
/// </summary>
public sealed class TokenRequestException(int status, string error, string? description, string? requestId)
    : Exception($"Token request refused, {status} {error}: {description} (request id {requestId ?? "unknown"})")
{
    public int Status { get; } = status;
    public string Error { get; } = error;
    public string? Description { get; } = description;
    public string? RequestId { get; } = requestId;
}
