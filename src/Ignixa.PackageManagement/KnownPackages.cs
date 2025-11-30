namespace Ignixa.PackageManagement;

/// <summary>
/// Known FHIR package identifiers that require special handling.
/// </summary>
public static class KnownPackages
{
    /// <summary>
    /// Core FHIR specification packages that contain pre-compiled conformance resources.
    /// These packages should not be loaded at runtime as they conflict with the embedded
    /// definitions in Ignixa.Specification.
    /// </summary>
    /// <remarks>
    /// Core packages include:
    /// - StructureDefinitions for all FHIR resources
    /// - SearchParameters for all defined search parameters
    /// - ValueSets and CodeSystems for terminologies
    /// - OperationDefinitions, CapabilityStatements, etc.
    ///
    /// Attempting to load these packages would create conflicts and duplicate data.
    /// Use Implementation Guide packages instead (e.g., hl7.fhir.us.core, ihe.iti.pdqm).
    /// </remarks>
    public static readonly HashSet<string> CorePackages = new(StringComparer.OrdinalIgnoreCase)
    {
        "hl7.fhir.r2.core",
        "hl7.fhir.r3.core",
        "hl7.fhir.r4.core",
        "hl7.fhir.r4b.core",
        "hl7.fhir.r5.core",
        "hl7.fhir.r6.core"
    };

    /// <summary>
    /// Checks if a package ID is a core FHIR specification package.
    /// </summary>
    /// <param name="packageId">Package identifier (e.g., "hl7.fhir.r4.core")</param>
    /// <returns>True if the package is a core FHIR specification package</returns>
    public static bool IsCorePackage(string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        return CorePackages.Contains(packageId);
    }
}
