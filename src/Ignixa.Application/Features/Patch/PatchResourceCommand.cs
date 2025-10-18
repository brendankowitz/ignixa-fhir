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
public record PatchResourceCommand(
    int TenantId,
    string ResourceType,
    string ResourceId,
    string PatchDocument) : IRequest<ResourceWrapper?>;
