using System;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Application.Features.Patch.Validation;

/// <summary>
/// Validates that immutable properties (id, meta.versionId, meta.lastUpdated) are not modified via PATCH.
/// </summary>
public class ImmutablePropertyValidator
{
    /// <summary>
    /// Validate that immutable properties have not changed between before and after states.
    /// </summary>
    /// <param name="before">Resource state before patch</param>
    /// <param name="after">Resource state after patch</param>
    /// <exception cref="FhirPatchException">Thrown if immutable properties were modified</exception>
    public void Validate(ResourceJsonNode before, ResourceJsonNode after)
    {
        // Validate id unchanged
        if (before.Id != after.Id)
        {
            throw new FhirPatchException(
                $"Cannot modify immutable property 'id' via PATCH (before: '{before.Id}', after: '{after.Id}'). Use PUT to change resource ID.");
        }

        // Validate meta.versionId unchanged (if both have meta)
        if (before.Meta.VersionId != null && after.Meta.VersionId != null)
        {
            if (before.Meta.VersionId != after.Meta.VersionId)
            {
                throw new FhirPatchException(
                    $"Cannot modify immutable property 'meta.versionId' via PATCH (server-managed). Before: '{before.Meta.VersionId}', After: '{after.Meta.VersionId}'");
            }
        }

        // Validate meta.lastUpdated unchanged (if both have meta)
        if (before.Meta.LastUpdated.HasValue && after.Meta.LastUpdated.HasValue)
        {
            // Allow small time differences (within 1 second) due to serialization
            var timeDifference = Math.Abs((before.Meta.LastUpdated.Value - after.Meta.LastUpdated.Value).TotalSeconds);
            if (timeDifference > 1)
            {
                throw new FhirPatchException(
                    $"Cannot modify immutable property 'meta.lastUpdated' via PATCH (server-managed). Before: '{before.Meta.LastUpdated}', After: '{after.Meta.LastUpdated}'");
            }
        }
    }
}
