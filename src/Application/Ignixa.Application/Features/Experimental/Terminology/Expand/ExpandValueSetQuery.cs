using System.Text.Json.Nodes;
using Medino;

namespace Ignixa.Application.Features.Experimental.Terminology.Expand;

/// <summary>
/// Query for ValueSet $expand operation.
/// Expands a ValueSet to a list of codes (pre-computed expansions when available).
/// </summary>
public record ExpandValueSetQuery(
    int TenantId,
    string Url,
    string? Filter = null,
    int? Count = null,
    int? Offset = null,
    bool IncludeDesignations = false) : IRequest<ExpandValueSetResult>;

/// <summary>
/// Result containing expanded ValueSet resource as JsonNode.
/// </summary>
public record ExpandValueSetResult(JsonNode ValueSetResource);
