// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Domain.Terminology;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlEntityFramework.Features.Terminology;

/// <summary>
/// SQL-based terminology service using imported TermConcept and TermValueSetExpansion tables.
/// Provides fast $lookup and $validate-code operations with in-memory caching.
/// </summary>
/// <remarks>
/// MULTI-TENANT ARCHITECTURE: Terminology resources (ValueSet, CodeSystem, ConceptMap) are
/// stored in the System Partition (Partition 0) and shared across all tenants. This service
/// uses SqlEntityFrameworkRepositoryFactory to create FhirDbContext instances scoped to the
/// system partition. See also: ImportTerminologyResourceActivity, PackageLoadedTerminologyImportHandler
/// </remarks>
public class SqlTerminologyService : ITerminologyService
{
    private readonly SqlEntityFrameworkRepositoryFactory _repositoryFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SqlTerminologyService> _logger;

    public SqlTerminologyService(
        SqlEntityFrameworkRepositoryFactory repositoryFactory,
        IMemoryCache cache,
        ILogger<SqlTerminologyService> logger)
    {
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates a FhirDbContext for the system partition (Partition 0) where terminology resources are stored.
    /// </summary>
    /// <remarks>
    /// Terminology resources (ValueSet, CodeSystem, ConceptMap) are stored in the System Partition and shared
    /// across all tenants. This method uses SqlEntityFrameworkRepositoryFactory.GetDbContextAsync to create
    /// a properly configured DbContext for the system partition.
    /// </remarks>
    private async Task<FhirDbContext> CreateSystemPartitionContextAsync(CancellationToken cancellationToken)
    {
        // Get a FhirDbContext for the system partition (Partition 0) where terminology is stored
        return await _repositoryFactory.GetDbContextAsync(SystemConstants.SystemPartitionId, cancellationToken);
    }

    /// <summary>
    /// $lookup operation - Look up concept by system and code.
    /// Uses TermConcept table with caching for performance.
    /// </summary>
    public async Task<LookupResult> LookupCodeAsync(
        string system,
        string code,
        string? version,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(system);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        // Cache key includes version for proper invalidation
        var cacheKey = $"lookup:{system}:{version ?? "latest"}:{code}";

        // Try cache first
        if (_cache.TryGetValue(cacheKey, out LookupResult? cachedResult) && cachedResult != null)
        {
            _logger.LogDebug("Cache hit for lookup: {System}|{Code}", system, code);
            return cachedResult;
        }

        // Get FhirDbContext for system partition (where terminology is stored)
        await using var context = await CreateSystemPartitionContextAsync(cancellationToken);

        // 1. Get SystemId from System table
        var systemId = await context.Systems
            .Where(s => s.Value == system)
            .Select(s => s.SystemId)
            .FirstOrDefaultAsync(cancellationToken);

        if (systemId == 0)
        {
            var notFoundResult = new LookupResult(Found: false, null, null, null, null, null, null);
            CacheResult(cacheKey, notFoundResult);
            return notFoundResult;
        }

        // 2. Query TermConcept by SystemId + Code (join to TermCodeSystem for metadata)
        var concept = await context.TermConcepts
            .Include(tc => tc.CodeSystem)
            .Where(tc => tc.CodeSystem.SystemId == systemId && tc.Code == code)
            .Where(tc => version == null || tc.CodeSystem.Version == version)
            .OrderByDescending(tc => tc.CodeSystem.ImportedDate) // Latest version if multiple
            .FirstOrDefaultAsync(cancellationToken);

        if (concept == null)
        {
            var notFoundResult = new LookupResult(Found: false, null, null, null, null, null, null);
            CacheResult(cacheKey, notFoundResult);
            return notFoundResult;
        }

        // 3. Parse properties/designations from PropertiesJson
        var (properties, designations) = ParsePropertiesJson(concept.PropertiesJson);

        var result = new LookupResult(
            Found: true,
            Name: null, // TODO: Add Name field to TermCodeSystem domain model and entity
            Version: concept.CodeSystem.Version,
            Display: concept.Display,
            Definition: concept.Definition,
            Properties: properties,
            Designations: designations);

        CacheResult(cacheKey, result);
        return result;
    }

    /// <summary>
    /// $expand operation - Expand a ValueSet to a list of codes.
    /// Uses TermValueSetExpansion table with pagination support.
    /// </summary>
    public async Task<ExpandResult?> ExpandValueSetAsync(
        ExpansionParameters parameters,
        CancellationToken cancellationToken)
    {
        // 1. Validate input
        ArgumentException.ThrowIfNullOrWhiteSpace(parameters.Url);

        // 2. Check cache first
        var cacheKey = $"expand:{parameters.Url}:{parameters.Filter ?? "none"}:{parameters.Count ?? 1000}:{parameters.Offset ?? 0}";

        if (_cache.TryGetValue(cacheKey, out ExpandResult? cachedResult) && cachedResult != null)
        {
            _logger.LogDebug("Cache hit for expand: {Url}", parameters.Url);
            return cachedResult;
        }

        // Get FhirDbContext for system partition (where terminology is stored)
        await using var context = await CreateSystemPartitionContextAsync(cancellationToken);

        try
        {
            // 3. Query TermValueSet table - find ValueSet by canonical URL
            var termValueSet = await context.TermValueSets
                .AsNoTracking()
                .Where(tvs => tvs.Canonical == parameters.Url && tvs.IsExpanded)
                .OrderByDescending(tvs => tvs.ImportedDate)
                .FirstOrDefaultAsync(cancellationToken);

            // 4. Return null if not found or not expanded
            if (termValueSet == null)
            {
                _logger.LogWarning("ValueSet '{Url}' not found or not expanded", parameters.Url);
                return null;
            }

            // 5. Build query for TermValueSetExpansion with filter and pagination
            var expansionQuery = context.TermValueSetExpansions
                .AsNoTracking()
                .Include(tvse => tvse.System)
                .Where(tvse => tvse.TermValueSetId == termValueSet.TermValueSetId);

            // Apply text filter if provided (filter on Code LIKE or Display LIKE)
            if (!string.IsNullOrWhiteSpace(parameters.Filter))
            {
                // Use EF.Functions.Like for case-insensitive SQL LIKE matching
                var filterPattern = $"%{parameters.Filter}%";
                expansionQuery = expansionQuery.Where(tvse =>
                    EF.Functions.Like(tvse.Code, filterPattern) ||
                    (tvse.Display != null && EF.Functions.Like(tvse.Display, filterPattern)));
            }

            // 6. Get total count (before pagination)
            var totalCount = await expansionQuery.CountAsync(cancellationToken);

            // 7. Apply pagination and ordering
            var count = parameters.Count ?? 1000; // Default page size
            var offset = parameters.Offset ?? 0;

            var expansionEntries = await expansionQuery
                .OrderBy(tvse => tvse.Code) // Consistent ordering for pagination
                .Skip(offset)
                .Take(count)
                .ToListAsync(cancellationToken);

            // 8. Map to ExpandedConcept records
            var expandedConcepts = expansionEntries
                .Select(e => new ExpandedConcept(
                    System: e.System.Value,
                    Code: e.Code,
                    Display: e.Display,
                    Version: e.SystemVersion,
                    Inactive: e.IsActive ? null : true)) // Only set Inactive if false
                .ToList();

            // 9. Create ExpandResult
            var result = new ExpandResult(
                Identifier: $"urn:uuid:{Guid.NewGuid()}", // Generate unique expansion identifier
                Timestamp: termValueSet.LastExpansionDate ?? termValueSet.ImportedDate,
                Total: totalCount,
                Offset: offset,
                Contains: expandedConcepts,
                Incomplete: termValueSet.IsPartialExpansion);

            // 10. Cache result (1-hour sliding expiration)
            CacheResult(cacheKey, result);

            _logger.LogInformation(
                "Expanded ValueSet '{Url}' - returned {Count}/{Total} codes (offset={Offset})",
                parameters.Url,
                expandedConcepts.Count,
                totalCount,
                offset);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error expanding ValueSet '{Url}'", parameters.Url);
            return null; // Graceful degradation
        }
    }

    /// <summary>
    /// $validate-code operation - Check if code is in ValueSet.
    /// Uses TermValueSetExpansion table with caching for performance.
    /// </summary>
    public async Task<TerminologyValidationResult> ValidateCodeAsync(
        string? system,
        string? code,
        string? display,
        string? valueSetUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(valueSetUrl))
        {
            return new TerminologyValidationResult(
                IsValid: false,
                Severity: IssueSeverity.Error,
                Message: "ValueSet URL is required");
        }

        if (string.IsNullOrEmpty(code))
        {
            return new TerminologyValidationResult(
                IsValid: false,
                Severity: IssueSeverity.Error,
                Message: "Code is required");
        }

        // Cache key
        var cacheKey = $"validate:{valueSetUrl}:{system ?? "any"}:{code}:{display ?? "none"}";

        // Try cache first
        if (_cache.TryGetValue(cacheKey, out TerminologyValidationResult? cachedResult) && cachedResult != null)
        {
            _logger.LogDebug("Cache hit for validate: {ValueSet}|{Code}", valueSetUrl, code);
            return cachedResult;
        }

        // Get FhirDbContext for system partition (where terminology is stored)
        await using var context = await CreateSystemPartitionContextAsync(cancellationToken);

        // 1. Find TermValueSet by canonical URL
        var termValueSet = await context.TermValueSets
            .Where(tvs => tvs.Canonical == valueSetUrl && tvs.IsExpanded)
            .OrderByDescending(tvs => tvs.ImportedDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (termValueSet == null)
        {
            var warningResult = new TerminologyValidationResult(
                IsValid: false,
                Severity: IssueSeverity.Warning,
                Message: $"ValueSet '{valueSetUrl}' not found or not expanded (terminology not imported)");
            CacheResult(cacheKey, warningResult);
            return warningResult;
        }

        // 2. Get SystemId if system is provided
        int? systemId = null;
        if (!string.IsNullOrEmpty(system))
        {
            systemId = await context.Systems
                .Where(s => s.Value == system)
                .Select(s => (int?)s.SystemId)
                .FirstOrDefaultAsync(cancellationToken);

            if (systemId == null)
            {
                var notFoundResult = new TerminologyValidationResult(
                    IsValid: false,
                    Severity: IssueSeverity.Error,
                    Message: $"System '{system}' not found");
                CacheResult(cacheKey, notFoundResult);
                return notFoundResult;
            }
        }

        // 3. Check if code exists in expansion
        var expansionEntry = await context.TermValueSetExpansions
            .Where(tvse =>
                tvse.TermValueSetId == termValueSet.TermValueSetId &&
                tvse.Code == code &&
                (systemId == null || tvse.SystemId == systemId))
            .FirstOrDefaultAsync(cancellationToken);

        if (expansionEntry == null)
        {
            var invalidResult = new TerminologyValidationResult(
                IsValid: false,
                Severity: IssueSeverity.Error,
                Message: $"Code '{code}' not found in ValueSet '{valueSetUrl}'");
            CacheResult(cacheKey, invalidResult);
            return invalidResult;
        }

        // 4. Validate display if provided
        if (!string.IsNullOrEmpty(display) && !string.IsNullOrEmpty(expansionEntry.Display))
        {
            if (!string.Equals(display, expansionEntry.Display, StringComparison.Ordinal))
            {
                var displayWarning = new TerminologyValidationResult(
                    IsValid: true, // Code is valid, but display mismatch is WARNING
                    Severity: IssueSeverity.Warning,
                    Message: $"Display '{display}' does not match expected '{expansionEntry.Display}'");
                CacheResult(cacheKey, displayWarning);
                return displayWarning;
            }
        }

        var validResult = new TerminologyValidationResult(
            IsValid: true,
            Severity: IssueSeverity.Information,
            Message: "Code is valid");
        CacheResult(cacheKey, validResult);
        return validResult;
    }

    /// <summary>
    /// Validates a coded element against a terminology binding.
    /// Uses ValidateCodeAsync for code validation and LookupCodeAsync for display validation.
    /// </summary>
    public async Task<BindingValidationResult> ValidateBindingAsync(
        string valueSetUrl,
        BindingStrength strength,
        string? system,
        string? code,
        string? display,
        string? version,
        CancellationToken cancellationToken)
    {
        // Step 1: Validate code against ValueSet
        var codeValidation = await ValidateCodeAsync(system, code, display, valueSetUrl, cancellationToken);

        // Step 2: Determine severity based on binding strength
        var (isValid, severity, message) = DetermineSeverityFromStrength(strength, codeValidation);

        // Step 3: Validate display if provided and code was found
        string? suggestedDisplay = null;
        if (codeValidation.IsValid && !string.IsNullOrEmpty(display) && !string.IsNullOrEmpty(system) && !string.IsNullOrEmpty(code))
        {
            // Look up correct display from CodeSystem
            var lookupResult = await LookupCodeAsync(system, code, version, cancellationToken);
            if (lookupResult.Found && !string.IsNullOrEmpty(lookupResult.Display))
            {
                // Case-sensitive display comparison (FHIR spec requirement)
                if (!string.Equals(display, lookupResult.Display, StringComparison.Ordinal))
                {
                    suggestedDisplay = lookupResult.Display;

                    // Display mismatch is WARNING, not ERROR
                    if (severity < IssueSeverity.Warning)
                    {
                        severity = IssueSeverity.Warning;
                    }

                    message = $"{message ?? "Code is valid"} However, display '{display}' does not match expected '{lookupResult.Display}'";
                }
            }
        }

        return new BindingValidationResult(
            IsValid: isValid,
            Strength: strength,
            Severity: severity,
            Message: message,
            SuggestedDisplay: suggestedDisplay);
    }

    /// <summary>
    /// Determines validation result severity based on binding strength and code validation outcome.
    /// </summary>
    private static (bool IsValid, IssueSeverity Severity, string? Message) DetermineSeverityFromStrength(
        BindingStrength strength,
        TerminologyValidationResult codeValidation)
    {
        return strength switch
        {
            BindingStrength.Required => codeValidation.IsValid
                ? (true, IssueSeverity.Information, codeValidation.Message)
                : (false, IssueSeverity.Error, codeValidation.Message),

            BindingStrength.Extensible => codeValidation.IsValid
                ? (true, IssueSeverity.Information, codeValidation.Message)
                : (true, IssueSeverity.Warning, codeValidation.Message), // Extensible allows custom codes with warning

            BindingStrength.Preferred => (true, IssueSeverity.Information, codeValidation.Message),

            BindingStrength.Example => (true, IssueSeverity.Information, null), // No validation for examples

            _ => (true, IssueSeverity.Warning, "Unknown binding strength")
        };
    }

    /// <summary>
    /// $translate operation - Translate code using ConceptMap.
    /// Queries TermConceptMapElement table for matching translations.
    /// </summary>
    public async Task<TranslateResult> TranslateCodeAsync(
        TranslateParameters parameters,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameters.Code);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameters.System);

        // Get FhirDbContext for system partition (where terminology is stored)
        await using var context = await CreateSystemPartitionContextAsync(cancellationToken);

        try
        {
            // Get SystemId for source system
            var sourceSystemId = await context.Systems
                .Where(s => s.Value == parameters.System)
                .Select(s => s.SystemId)
                .FirstOrDefaultAsync(cancellationToken);

            if (sourceSystemId == 0)
            {
                return new TranslateResult(
                    Result: false,
                    Message: $"Source system '{parameters.System}' not found",
                    Matches: Array.Empty<TranslateMatch>());
            }

            // Build query for translation
            var query = context.TermConceptMapElements
                .Include(e => e.ConceptMap)
                .Include(e => e.SourceSystem)
                .Include(e => e.TargetSystem)
                .AsNoTracking();

            if (parameters.Reverse)
            {
                // Reverse translation: target → source
                query = query.Where(e =>
                    e.TargetSystemId == sourceSystemId &&
                    e.TargetCode == parameters.Code);
            }
            else
            {
                // Forward translation: source → target
                query = query.Where(e =>
                    e.SourceSystemId == sourceSystemId &&
                    e.SourceCode == parameters.Code);
            }

            // Filter by target system if specified
            if (!string.IsNullOrEmpty(parameters.TargetSystem))
            {
                var targetSystemId = await context.Systems
                    .Where(s => s.Value == parameters.TargetSystem)
                    .Select(s => s.SystemId)
                    .FirstOrDefaultAsync(cancellationToken);

                if (targetSystemId != 0)
                {
                    query = parameters.Reverse
                        ? query.Where(e => e.SourceSystemId == targetSystemId)
                        : query.Where(e => e.TargetSystemId == targetSystemId);
                }
            }

            // Execute query
            var mappings = await query.ToListAsync(cancellationToken);

            if (mappings.Count == 0)
            {
                return new TranslateResult(
                    Result: false,
                    Message: "No translation found",
                    Matches: Array.Empty<TranslateMatch>());
            }

            // Map to TranslateMatch results
            var matches = mappings.Select(m =>
            {
                var (system, code, display) = parameters.Reverse
                    ? (m.SourceSystem.Value, m.SourceCode, m.SourceDisplay)
                    : (m.TargetSystem?.Value ?? "unknown", m.TargetCode ?? "unknown", m.TargetDisplay);

                return new TranslateMatch(
                    Equivalence: m.Equivalence,
                    Concept: new TranslateConcept(system, code, display),
                    Source: m.ConceptMap.Canonical,
                    Comment: m.Comment);
            }).ToList();

            return new TranslateResult(
                Result: true,
                Message: $"Found {matches.Count} translation(s)",
                Matches: matches);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error translating code {System}|{Code}", parameters.System, parameters.Code);
            return new TranslateResult(
                Result: false,
                Message: $"Translation error: {ex.Message}",
                Matches: Array.Empty<TranslateMatch>());
        }
    }

    /// <summary>
    /// $subsumes operation - Test hierarchical relationship between codes.
    /// Uses ParentConceptId to traverse hierarchy.
    /// </summary>
    public async Task<SubsumesResult> SubsumesAsync(
        SubsumesParameters parameters,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameters.CodeA);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameters.CodeB);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameters.System);

        // Get FhirDbContext for system partition (where terminology is stored)
        await using var context = await CreateSystemPartitionContextAsync(cancellationToken);

        try
        {
            // Get SystemId
            var systemId = await context.Systems
                .Where(s => s.Value == parameters.System)
                .Select(s => s.SystemId)
                .FirstOrDefaultAsync(cancellationToken);

            if (systemId == 0)
            {
                return new SubsumesResult("not-subsumed");
            }

            // Find both concepts
            var concepts = await context.TermConcepts
                .Where(tc => tc.CodeSystem.SystemId == systemId)
                .Where(tc => tc.Code == parameters.CodeA || tc.Code == parameters.CodeB)
                .Where(tc => parameters.Version == null || tc.CodeSystem.Version == parameters.Version)
                .ToListAsync(cancellationToken);

            var conceptA = concepts.FirstOrDefault(c => c.Code == parameters.CodeA);
            var conceptB = concepts.FirstOrDefault(c => c.Code == parameters.CodeB);

            if (conceptA == null || conceptB == null)
            {
                return new SubsumesResult("not-subsumed");
            }

            // Check if equivalent (same concept)
            if (conceptA.TermConceptId == conceptB.TermConceptId)
            {
                return new SubsumesResult("equivalent");
            }

            // Check if A subsumes B (B is descendant of A)
            if (await IsDescendantOfAsync(context, conceptB.TermConceptId, conceptA.TermConceptId, cancellationToken))
            {
                return new SubsumesResult("subsumes");
            }

            // Check if B subsumes A (A is descendant of B)
            if (await IsDescendantOfAsync(context, conceptA.TermConceptId, conceptB.TermConceptId, cancellationToken))
            {
                return new SubsumesResult("subsumed-by");
            }

            // No relationship
            return new SubsumesResult("not-subsumed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking subsumption for {System}|{CodeA} vs {CodeB}",
                parameters.System, parameters.CodeA, parameters.CodeB);
            return new SubsumesResult("not-subsumed");
        }
    }

    /// <summary>
    /// Checks if descendantId is a descendant of ancestorId by traversing parent references.
    /// </summary>
    private async Task<bool> IsDescendantOfAsync(
        FhirDbContext context,
        long descendantId,
        long ancestorId,
        CancellationToken cancellationToken)
    {
        var currentId = descendantId;
        var visited = new HashSet<long> { currentId }; // Prevent infinite loops

        // Traverse up the hierarchy (max 50 levels to prevent infinite loops)
        for (int depth = 0; depth < 50; depth++)
        {
            // Get parent of current concept
            var parentId = await context.TermConcepts
                .Where(tc => tc.TermConceptId == currentId)
                .Select(tc => tc.ParentConceptId)
                .FirstOrDefaultAsync(cancellationToken);

            if (parentId == null)
            {
                // Reached root, no match found
                return false;
            }

            if (parentId == ancestorId)
            {
                // Found ancestor!
                return true;
            }

            if (visited.Contains(parentId.Value))
            {
                // Circular reference detected, abort
                _logger.LogWarning("Circular parent reference detected in TermConcept hierarchy at {ConceptId}", currentId);
                return false;
            }

            visited.Add(parentId.Value);
            currentId = parentId.Value;
        }

        // Max depth exceeded
        _logger.LogWarning("Max hierarchy depth (50) exceeded while checking subsumption");
        return false;
    }

    /// <summary>
    /// Get import status for a canonical resource.
    /// Used by HybridTerminologyService to route SQL vs JSON fallback.
    /// </summary>
    public async Task<TerminologyImportStatus?> GetImportStatusAsync(
        string canonical,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonical);

        // Get FhirDbContext for system partition (where terminology is stored)
        await using var context = await CreateSystemPartitionContextAsync(cancellationToken);

        var statusString = await context.PackageResources
            .Where(pr => pr.Canonical == canonical && pr.IsActive)
            .OrderByDescending(pr => pr.LoadedDate)
            .Select(pr => pr.TerminologyImportStatus)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrEmpty(statusString))
        {
            return null;
        }

        // Parse string to enum (Entity stores as string, Domain uses enum)
        if (Enum.TryParse<TerminologyImportStatus>(statusString, out var status))
        {
            return status;
        }

        _logger.LogWarning("Invalid TerminologyImportStatus value: {Status}", statusString);
        return null;
    }

    /// <summary>
    /// Parse PropertiesJson column to extract properties and designations.
    /// Format: { "property": [...], "designation": [...] }
    /// </summary>
    private (IReadOnlyList<PropertyValue>?, IReadOnlyList<Designation>?) ParsePropertiesJson(string? propertiesJson)
    {
        if (string.IsNullOrEmpty(propertiesJson))
        {
            return (null, null);
        }

        try
        {
            var json = JsonNode.Parse(propertiesJson)?.AsObject();
            if (json == null)
            {
                return (null, null);
            }

            // Parse properties
            List<PropertyValue>? properties = null;
            var propertyArray = json["property"]?.AsArray();
            if (propertyArray != null && propertyArray.Count > 0)
            {
                properties = new List<PropertyValue>();
                foreach (var prop in propertyArray)
                {
                    var code = prop?["code"]?.GetValue<string>();
                    var value = prop?["valueString"]?.GetValue<string>()
                        ?? prop?["valueCode"]?.GetValue<string>()
                        ?? prop?["valueCoding"]?["code"]?.GetValue<string>()
                        ?? prop?["valueBoolean"]?.GetValue<bool>().ToString();

                    if (code != null)
                    {
                        properties.Add(new PropertyValue(code, value));
                    }
                }
            }

            // Parse designations
            List<Designation>? designations = null;
            var designationArray = json["designation"]?.AsArray();
            if (designationArray != null && designationArray.Count > 0)
            {
                designations = new List<Designation>();
                foreach (var desig in designationArray)
                {
                    var language = desig?["language"]?.GetValue<string>();
                    var use = desig?["use"]?["code"]?.GetValue<string>();
                    var value = desig?["value"]?.GetValue<string>();

                    if (value != null)
                    {
                        designations.Add(new Designation(language, use, value));
                    }
                }
            }

            return (properties, designations);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse PropertiesJson, returning null");
            return (null, null);
        }
    }

    /// <summary>
    /// Cache a result with 1-hour sliding expiration.
    /// </summary>
    private void CacheResult<T>(string cacheKey, T result)
    {
        var cacheOptions = new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromHours(1),
            Size = 1 // For cache size limit tracking
        };

        _cache.Set(cacheKey, result, cacheOptions);
    }
}
