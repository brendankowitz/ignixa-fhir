using System;
using System.Collections.Immutable;

namespace Ignixa.Application.Features.Patch.Validation;

/// <summary>
/// Checks if a FHIRPath expression references an immutable property.
/// Used for pre-validation in PATCH executors to reject operations targeting protected fields.
/// Per FHIR spec, certain fields are immutable and cannot be modified via PATCH.
/// </summary>
public static class ImmutablePathChecker
{
    /// <summary>
    /// Immutable path patterns that cannot be modified via PATCH operations.
    /// These are protected fields per FHIR specification.
    /// </summary>
    private static readonly ImmutableArray<string> ImmutablePathPatterns =
    [
        ".ID",
        ".META.VERSIONID",
        ".META.LASTUPDATED"
    ];

    /// <summary>
    /// Check if FHIRPath references an immutable property that cannot be modified via PATCH.
    /// </summary>
    /// <param name="fhirPathExpression">FHIRPath expression to check</param>
    /// <returns>True if the path references an immutable property</returns>
    public static bool IsImmutablePath(string fhirPathExpression)
    {
        ArgumentNullException.ThrowIfNull(fhirPathExpression);

        // Check for immutable properties (case-insensitive)
        var upperPath = fhirPathExpression.ToUpperInvariant();

        foreach (var pattern in ImmutablePathPatterns)
        {
            if (upperPath.Contains(pattern, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
