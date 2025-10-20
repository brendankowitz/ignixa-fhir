using Ignixa.Domain.Models;
using Medino;

namespace Ignixa.Application.Features.Patch;

/// <summary>
/// Command to patch a FHIR resource using FHIRPath Patch operations.
/// </summary>
/// <param name="TenantId">Tenant ID (partition identifier)</param>
/// <param name="ResourceType">FHIR resource type (e.g., "Patient", "Observation")</param>
/// <param name="ResourceId">Logical ID of the resource to patch</param>
/// <param name="PatchDocument">Parameters resource JSON containing patch operations</param>
/// <param name="IfMatch">Optional ETag for optimistic concurrency control. If specified, patch only succeeds if resource version matches. Format: version ID (e.g., "5"), not weak ETag format.</param>
public record PatchResourceCommand(
    int TenantId,
    string ResourceType,
    string ResourceId,
    string PatchDocument,
    string? IfMatch = null) : IRequest<ResourceWrapper?>;
