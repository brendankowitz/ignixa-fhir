using System.Text.Json.Nodes;
using Medino;

namespace Ignixa.Application.Operations.Features.Terminology.Subsumes;

/// <summary>
/// Query for CodeSystem $subsumes operation.
/// Tests subsumption relationship between two codes.
/// </summary>
public record SubsumesQuery(
    int TenantId,
    string CodeA,
    string CodeB,
    string System,
    string? Version = null) : IRequest<SubsumesQueryResult>;

/// <summary>
/// Result containing Parameters resource as JsonNode.
/// </summary>
public record SubsumesQueryResult(JsonNode ParametersResource);
