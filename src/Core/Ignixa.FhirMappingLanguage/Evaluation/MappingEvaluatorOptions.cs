/* Copyright (c) 2025, Ignixa Contributors */

namespace Ignixa.FhirMappingLanguage.Evaluation;

/// <summary>
/// Configuration options for mapping evaluation with security and performance controls.
/// </summary>
public class MappingEvaluatorOptions
{
    /// <summary>
    /// Default instance with recommended security settings.
    /// </summary>
    public static MappingEvaluatorOptions Default => new();

    // ========== Resource Limits ==========

    /// <summary>
    /// Maximum recursion depth for group calls (default: 50).
    /// Prevents infinite recursion attacks.
    /// </summary>
    public int MaxRecursionDepth { get; set; } = 50;

    /// <summary>
    /// Maximum number of elements that can be created during transformation (default: 100,000).
    /// Prevents memory exhaustion attacks.
    /// </summary>
    public int MaxElementsCreated { get; set; } = 100_000;

    /// <summary>
    /// Maximum size of a compiled map in bytes (default: 50 MB).
    /// Prevents loading of excessively large maps.
    /// </summary>
    public long MaxMapSizeBytes { get; set; } = 50_000_000;

    /// <summary>
    /// Maximum size of input resource in bytes (default: 10 MB).
    /// Prevents processing of excessively large resources.
    /// </summary>
    public long MaxInputResourceSizeBytes { get; set; } = 10_000_000;

    // ========== Timeout Settings ==========

    /// <summary>
    /// Timeout for entire transformation operation (default: 30 seconds).
    /// Prevents long-running transformations from consuming resources.
    /// </summary>
    public TimeSpan TransformTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Timeout for individual FHIRPath expression evaluation (default: 5 seconds).
    /// Prevents expensive FHIRPath queries from blocking.
    /// </summary>
    public TimeSpan FhirPathTimeout { get; set; } = TimeSpan.FromSeconds(5);

    // ========== Import Security ==========

    /// <summary>
    /// Allowed domains for HTTP/HTTPS imports (default: hl7.org, fhir.org).
    /// Only maps from these domains can be imported via HTTP.
    /// </summary>
    public ISet<string> AllowedImportDomains { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "hl7.org",
        "fhir.org",
        "build.fhir.org"
    };

    /// <summary>
    /// Allow file:// imports.
    /// WARNING: Enabling this can expose local file system.
    /// </summary>
    public bool AllowFileSystemImports { get; set; }

    /// <summary>
    /// Sandboxed directory for file system imports when enabled.
    /// Only files under this directory can be imported.
    /// </summary>
    public string? FileSystemImportSandbox { get; set; }

    // ========== ConceptMap Security ==========

    /// <summary>
    /// Allowed target systems for ConceptMap translation (default: common FHIR terminologies).
    /// Supports wildcards (e.g., "http://hl7.org/fhir/*").
    /// </summary>
    public ISet<string> AllowedConceptMapTargetSystems { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "http://snomed.info/sct",
        "http://loinc.org",
        "http://hl7.org/fhir/*",
        "http://unitsofmeasure.org",
        "http://terminology.hl7.org/*",
        "urn:oid:*"  // Allow OID-based systems
    };

    /// <summary>
    /// Maximum length for code values in translate() function (default: 100).
    /// Prevents data exfiltration via oversized codes.
    /// </summary>
    public int MaxCodeLength { get; set; } = 100;

    // ========== Error Handling ==========

    /// <summary>
    /// Error handling mode (default: Strict).
    /// Strict: Throw on first error.
    /// Lenient: Collect errors and continue.
    /// </summary>
    public ErrorMode ErrorMode { get; set; } = ErrorMode.Strict;

    /// <summary>
    /// Maximum number of errors to collect in Lenient mode (default: 100).
    /// Prevents memory exhaustion from collecting too many errors.
    /// </summary>
    public int MaxErrorsCollected { get; set; } = 100;

    // ========== Validation Methods ==========

    /// <summary>
    /// Validates that the options are correctly configured.
    /// </summary>
    public void Validate()
    {
        if (MaxRecursionDepth <= 0)
            throw new ArgumentException("MaxRecursionDepth must be positive", nameof(MaxRecursionDepth));

        if (MaxElementsCreated <= 0)
            throw new ArgumentException("MaxElementsCreated must be positive", nameof(MaxElementsCreated));

        if (MaxMapSizeBytes <= 0)
            throw new ArgumentException("MaxMapSizeBytes must be positive", nameof(MaxMapSizeBytes));

        if (MaxInputResourceSizeBytes <= 0)
            throw new ArgumentException("MaxInputResourceSizeBytes must be positive", nameof(MaxInputResourceSizeBytes));

        if (TransformTimeout <= TimeSpan.Zero)
            throw new ArgumentException("TransformTimeout must be positive", nameof(TransformTimeout));

        if (FhirPathTimeout <= TimeSpan.Zero)
            throw new ArgumentException("FhirPathTimeout must be positive", nameof(FhirPathTimeout));

        if (MaxCodeLength <= 0)
            throw new ArgumentException("MaxCodeLength must be positive", nameof(MaxCodeLength));

        if (MaxErrorsCollected <= 0)
            throw new ArgumentException("MaxErrorsCollected must be positive", nameof(MaxErrorsCollected));

        if (AllowFileSystemImports && string.IsNullOrWhiteSpace(FileSystemImportSandbox))
            throw new ArgumentException("FileSystemImportSandbox must be specified when AllowFileSystemImports is true");
    }

    /// <summary>
    /// Checks if a domain is allowed for imports.
    /// </summary>
    public bool IsDomainAllowed(string domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        return AllowedImportDomains.Contains(domain);
    }

    /// <summary>
    /// Checks if a target system is allowed for ConceptMap translation.
    /// Supports wildcard matching (e.g., "http://hl7.org/fhir/*").
    /// </summary>
    public bool IsTargetSystemAllowed(string targetSystem)
    {
        ArgumentNullException.ThrowIfNull(targetSystem);

        // If empty set, allow all
        if (AllowedConceptMapTargetSystems.Count == 0)
            return true;

        // Check exact match first
        if (AllowedConceptMapTargetSystems.Contains(targetSystem))
            return true;

        // Check wildcard matches
        return AllowedConceptMapTargetSystems
            .Where(allowed => allowed.EndsWith('*'))
            .Any(allowed => targetSystem.StartsWith(allowed.TrimEnd('*'), StringComparison.OrdinalIgnoreCase));
    }
}
