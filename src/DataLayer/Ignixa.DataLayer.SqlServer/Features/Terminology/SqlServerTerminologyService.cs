using System.Text.Json;
using Ignixa.Domain.Terminology;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer.Features.Terminology;

/// <summary>
/// <see cref="ITerminologyService"/> over <see cref="ISqlExecutionService"/>, replacing the EF
/// implementation. Verified against the 31-fact oracle captured in Phase F Task 5b, which recorded the EF
/// implementation's behaviour before it was replaced.
/// <para>
/// Terminology lives in the <b>system partition</b> rather than a tenant: the EF version resolved every
/// context through <c>GetDbContextAsync(SystemConstants.SystemPartitionId)</c>, so this takes the partition
/// id at construction and uses it for every query. It no longer needs the composition root — that coupling
/// is why the implementation had no test coverage before Task 5b.
/// </para>
/// <para>
/// Result caching is preserved exactly, including its keys. <c>LookupCodeAsync</c> memoises on
/// <c>system|version|code</c> and returns before touching the database on a hit, which is observable: a
/// caller that imports a CodeSystem after a miss still sees the miss until the entry expires.
/// </para>
/// </summary>
public sealed class SqlServerTerminologyService(
    ISqlExecutionService sqlExecutionService,
    int systemPartitionId,
    IMemoryCache cache,
    ILogger<SqlServerTerminologyService> logger) : ITerminologyService
{
    private static readonly TableDescriptor Systems = SqlCatalog.Default.Table("System");
    private static readonly TableDescriptor CodeSystems = SqlCatalog.Default.Table("TermCodeSystem");
    private static readonly TableDescriptor Concepts = SqlCatalog.Default.Table("TermConcept");
    private static readonly TableDescriptor ValueSets = SqlCatalog.Default.Table("TermValueSet");
    private static readonly TableDescriptor Expansions = SqlCatalog.Default.Table("TermValueSetExpansion");
    private static readonly TableDescriptor MapElements = SqlCatalog.Default.Table("TermConceptMapElement");
    private static readonly TableDescriptor Packages = SqlCatalog.Default.Table("PackageResource");

    // Every SQL string here is assembled from catalog identifiers and fixed literals; all caller data flows
    // through parameters. Stating the CA2100 justification once rather than at each call site.
    private static SqlCommand Command(string sql)
    {
#pragma warning disable CA2100
        return new SqlCommand(sql);
#pragma warning restore CA2100
    }

    public async Task<LookupResult> LookupCodeAsync(
        string system, string code, string? version, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(system);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var cacheKey = $"lookup:{system}:{version ?? "latest"}:{code}";
        if (cache.TryGetValue(cacheKey, out LookupResult? cached) && cached is not null)
        {
            logger.LogDebug("Cache hit for lookup: {System}|{Code}", system, code);
            return cached;
        }

        var systemId = await ResolveSystemIdAsync(system, cancellationToken);
        if (systemId is null)
        {
            return CacheAndReturn(cacheKey, NotFound());
        }

        var versionFilter = version is null
            ? string.Empty
            : $" AND cs.{CodeSystems.Column("Version").Name} = @version";

        // Ordered by the code system's import date so the most recently imported version wins when a
        // concept appears in more than one, matching the EF query's OrderByDescending(ImportedDate).
        using var command = Command(
            $"SELECT TOP 1 tc.{Concepts.Column("Display").Name}, tc.{Concepts.Column("Definition").Name}, " +
            $"tc.{Concepts.Column("PropertiesJson").Name}, cs.{CodeSystems.Column("Version").Name} " +
            $"FROM {Qualified(Concepts)} tc " +
            $"JOIN {Qualified(CodeSystems)} cs ON cs.{CodeSystems.Column("TermCodeSystemId").Name} = tc.{Concepts.Column("TermCodeSystemId").Name} " +
            $"WHERE cs.{CodeSystems.Column("SystemId").Name} = @systemId AND tc.{Concepts.Column("Code").Name} = @code" +
            versionFilter +
            $" ORDER BY cs.{CodeSystems.Column("ImportedDate").Name} DESC");

        command.Parameters.AddWithValue("@systemId", systemId.Value);
        command.Parameters.AddWithValue("@code", code);
        if (version is not null)
        {
            command.Parameters.AddWithValue("@version", version);
        }

        var rows = await sqlExecutionService.ExecuteReaderAsync(
            systemPartitionId,
            command,
            reader => (
                Display: reader.IsDBNull(0) ? null : reader.GetString(0),
                Definition: reader.IsDBNull(1) ? null : reader.GetString(1),
                PropertiesJson: reader.IsDBNull(2) ? null : reader.GetString(2),
                Version: reader.IsDBNull(3) ? null : reader.GetString(3)),
            cancellationToken);

        if (rows.Count == 0)
        {
            return CacheAndReturn(cacheKey, NotFound());
        }

        var row = rows[0];
        var (properties, designations) = ParsePropertiesJson(row.PropertiesJson);

        // Name stays null: TermCodeSystem carries no name column, which the EF implementation noted as a
        // gap rather than filled.
        return CacheAndReturn(cacheKey, new LookupResult(
            Found: true,
            Name: null,
            Version: row.Version,
            Display: row.Display,
            Definition: row.Definition,
            Properties: properties,
            Designations: designations));

        static LookupResult NotFound() => new(false, null, null, null, null, null, null);
    }

    public async Task<ExpandResult?> ExpandValueSetAsync(
        ExpansionParameters parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameters.Url);

        var cacheKey = $"expand:{parameters.Url}:{parameters.Filter ?? "none"}:{parameters.Count ?? 1000}:{parameters.Offset ?? 0}";
        if (cache.TryGetValue(cacheKey, out ExpandResult? cached) && cached is not null)
        {
            logger.LogDebug("Cache hit for expand: {Url}", parameters.Url);
            return cached;
        }

        var valueSet = await ReadValueSetAsync(parameters.Url, cancellationToken);
        if (valueSet is null)
        {
            // Null covers both "never imported" and "imported but not expanded"; the caller cannot tell
            // them apart, which the oracle pins as current behaviour.
            logger.LogWarning("ValueSet '{Url}' not found or not expanded", parameters.Url);
            return null;
        }

        var filterClause = string.IsNullOrWhiteSpace(parameters.Filter)
            ? string.Empty
            : $" AND (e.{Expansions.Column("Code").Name} LIKE @filter OR " +
              $"(e.{Expansions.Column("Display").Name} IS NOT NULL AND e.{Expansions.Column("Display").Name} LIKE @filter))";

        var total = await CountExpansionAsync(valueSet.Value.Id, filterClause, parameters.Filter, cancellationToken);

        var count = parameters.Count ?? 1000;
        var offset = parameters.Offset ?? 0;

        using var command = Command(
            $"SELECT s.{Systems.Column("Value").Name}, e.{Expansions.Column("Code").Name}, " +
            $"e.{Expansions.Column("Display").Name}, e.{Expansions.Column("SystemVersion").Name}, " +
            $"e.{Expansions.Column("IsActive").Name} " +
            $"FROM {Qualified(Expansions)} e " +
            $"JOIN {Qualified(Systems)} s ON s.{Systems.Column("SystemId").Name} = e.{Expansions.Column("SystemId").Name} " +
            $"WHERE e.{Expansions.Column("TermValueSetId").Name} = @valueSetId{filterClause} " +
            $"ORDER BY e.{Expansions.Column("Code").Name} " +
            "OFFSET @offset ROWS FETCH NEXT @count ROWS ONLY");

        command.Parameters.AddWithValue("@valueSetId", valueSet.Value.Id);
        command.Parameters.AddWithValue("@offset", offset);
        command.Parameters.AddWithValue("@count", count);
        if (!string.IsNullOrWhiteSpace(parameters.Filter))
        {
            command.Parameters.AddWithValue("@filter", $"%{parameters.Filter}%");
        }

        var contains = await sqlExecutionService.ExecuteReaderAsync(
            systemPartitionId,
            command,
            reader => new ExpandedConcept(
                System: reader.GetString(0),
                Code: reader.GetString(1),
                Display: reader.IsDBNull(2) ? null : reader.GetString(2),
                Version: reader.IsDBNull(3) ? null : reader.GetString(3),
                // Inactive is only set when the concept is inactive, never set to false.
                Inactive: reader.GetBoolean(4) ? null : true),
            cancellationToken);

        var result = new ExpandResult(
            Identifier: $"urn:uuid:{Guid.NewGuid()}",
            Timestamp: valueSet.Value.LastExpansionDate ?? valueSet.Value.ImportedDate,
            Total: total,
            Offset: offset,
            Contains: contains,
            Incomplete: valueSet.Value.IsPartialExpansion);

        return CacheAndReturn(cacheKey, result);
    }

    public async Task<TerminologyValidationResult> ValidateCodeAsync(
        string? system, string? code, string? display, string? valueSetUrl, CancellationToken cancellationToken)
    {
        // Validation is ValueSet-scoped only: there is no CodeSystem-only path, and the refusal below is
        // returned as an invalid result rather than an argument exception, so a caller that omits the
        // ValueSet cannot distinguish it from a genuinely invalid code without reading Message.
        if (string.IsNullOrEmpty(valueSetUrl))
        {
            return new TerminologyValidationResult(false, IssueSeverity.Error, "ValueSet URL is required");
        }

        if (string.IsNullOrEmpty(code))
        {
            return new TerminologyValidationResult(false, IssueSeverity.Error, "Code is required");
        }

        var cacheKey = $"validate:{valueSetUrl}:{system ?? "any"}:{code}:{display ?? "none"}";
        if (cache.TryGetValue(cacheKey, out TerminologyValidationResult? cached) && cached is not null)
        {
            logger.LogDebug("Cache hit for validate: {ValueSet}|{Code}", valueSetUrl, code);
            return cached;
        }

        var valueSet = await ReadValueSetAsync(valueSetUrl, cancellationToken);
        if (valueSet is null)
        {
            // Warning rather than Error: an unimported ValueSet is a deployment gap, not bad input.
            return CacheAndReturn(cacheKey, new TerminologyValidationResult(
                false, IssueSeverity.Warning,
                $"ValueSet '{valueSetUrl}' not found or not expanded (terminology not imported)"));
        }

        int? systemId = null;
        if (!string.IsNullOrEmpty(system))
        {
            systemId = await ResolveSystemIdAsync(system, cancellationToken);
            if (systemId is null)
            {
                return CacheAndReturn(cacheKey, new TerminologyValidationResult(
                    false, IssueSeverity.Error, $"System '{system}' not found"));
            }
        }

        var systemFilter = systemId is null
            ? string.Empty
            : $" AND e.{Expansions.Column("SystemId").Name} = @systemId";

        using var command = Command(
            $"SELECT TOP 1 e.{Expansions.Column("Display").Name} FROM {Qualified(Expansions)} e " +
            $"WHERE e.{Expansions.Column("TermValueSetId").Name} = @valueSetId " +
            $"AND e.{Expansions.Column("Code").Name} = @code{systemFilter}");
        command.Parameters.AddWithValue("@valueSetId", valueSet.Value.Id);
        command.Parameters.AddWithValue("@code", code);
        if (systemId is not null)
        {
            command.Parameters.AddWithValue("@systemId", systemId.Value);
        }

        var matches = await sqlExecutionService.ExecuteReaderAsync(
            systemPartitionId, command, reader => reader.IsDBNull(0) ? null : reader.GetString(0), cancellationToken);

        if (matches.Count == 0)
        {
            return CacheAndReturn(cacheKey, new TerminologyValidationResult(
                false, IssueSeverity.Error, $"Code '{code}' not found in ValueSet '{valueSetUrl}'"));
        }

        var expectedDisplay = matches[0];

        // A display mismatch is a warning on a *valid* code, not an invalid one.
        if (!string.IsNullOrEmpty(display)
            && !string.IsNullOrEmpty(expectedDisplay)
            && !string.Equals(display, expectedDisplay, StringComparison.Ordinal))
        {
            return CacheAndReturn(cacheKey, new TerminologyValidationResult(
                true, IssueSeverity.Warning, $"Display '{display}' does not match expected '{expectedDisplay}'"));
        }

        return CacheAndReturn(cacheKey, new TerminologyValidationResult(
            true, IssueSeverity.Information, "Code is valid"));
    }

    public async Task<BindingValidationResult> ValidateBindingAsync(
        string valueSetUrl,
        BindingStrength strength,
        string? system,
        string? code,
        string? display,
        string? version,
        CancellationToken cancellationToken)
    {
        // Composition over the two operations above rather than its own queries, exactly as before.
        var codeValidation = await ValidateCodeAsync(system, code, display, valueSetUrl, cancellationToken);
        var (isValid, severity, message) = DetermineSeverityFromStrength(strength, codeValidation);

        string? suggestedDisplay = null;

        if (codeValidation.IsValid
            && !string.IsNullOrEmpty(display)
            && !string.IsNullOrEmpty(system)
            && !string.IsNullOrEmpty(code))
        {
            var lookup = await LookupCodeAsync(system, code, version, cancellationToken);

            if (lookup.Found
                && !string.IsNullOrEmpty(lookup.Display)
                && !string.Equals(display, lookup.Display, StringComparison.Ordinal))
            {
                suggestedDisplay = lookup.Display;
                if (severity < IssueSeverity.Warning)
                {
                    severity = IssueSeverity.Warning;
                }

                message = $"{message ?? "Code is valid"} However, display '{display}' does not match expected '{lookup.Display}'";
            }
        }

        return new BindingValidationResult(isValid, strength, severity, message, suggestedDisplay);
    }

    public async Task<TranslateResult> TranslateCodeAsync(
        TranslateParameters parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var sourceSystemId = await ResolveSystemIdAsync(parameters.System, cancellationToken);
        if (sourceSystemId is null)
        {
            return new TranslateResult(false, $"Source system '{parameters.System}' not found", []);
        }

        // Reverse swaps which side of the mapping the supplied code is matched against.
        var matchColumn = parameters.Reverse ? "TargetSystemId" : "SourceSystemId";
        var codeColumn = parameters.Reverse ? "TargetCode" : "SourceCode";
        var narrowColumn = parameters.Reverse ? "SourceSystemId" : "TargetSystemId";

        var targetFilter = string.Empty;
        int? targetSystemId = null;
        if (!string.IsNullOrEmpty(parameters.TargetSystem))
        {
            targetSystemId = await ResolveSystemIdAsync(parameters.TargetSystem, cancellationToken);

            // An unresolvable target system is ignored rather than treated as "no matches", matching the
            // EF version's `if (targetSystemId != 0)` guard.
            if (targetSystemId is not null)
            {
                targetFilter = $" AND e.{MapElements.Column(narrowColumn).Name} = @targetSystemId";
            }
        }

        var maps = SqlCatalog.Default.Table("TermConceptMap");

        using var command = Command(
            $"SELECT e.{MapElements.Column("SourceCode").Name}, e.{MapElements.Column("SourceDisplay").Name}, " +
            $"e.{MapElements.Column("TargetCode").Name}, e.{MapElements.Column("TargetDisplay").Name}, " +
            $"e.{MapElements.Column("Equivalence").Name}, e.{MapElements.Column("Comment").Name}, " +
            $"ss.{Systems.Column("Value").Name}, ts.{Systems.Column("Value").Name}, " +
            $"cm.{maps.Column("Canonical").Name} " +
            $"FROM {Qualified(MapElements)} e " +
            $"JOIN {Qualified(maps)} cm ON cm.{maps.Column("TermConceptMapId").Name} = e.{MapElements.Column("TermConceptMapId").Name} " +
            $"JOIN {Qualified(Systems)} ss ON ss.{Systems.Column("SystemId").Name} = e.{MapElements.Column("SourceSystemId").Name} " +
            $"LEFT JOIN {Qualified(Systems)} ts ON ts.{Systems.Column("SystemId").Name} = e.{MapElements.Column("TargetSystemId").Name} " +
            $"WHERE e.{MapElements.Column(matchColumn).Name} = @sourceSystemId " +
            $"AND e.{MapElements.Column(codeColumn).Name} = @code{targetFilter}");

        command.Parameters.AddWithValue("@sourceSystemId", sourceSystemId.Value);
        command.Parameters.AddWithValue("@code", parameters.Code);
        if (targetSystemId is not null && targetFilter.Length > 0)
        {
            command.Parameters.AddWithValue("@targetSystemId", targetSystemId.Value);
        }

        var rows = await sqlExecutionService.ExecuteReaderAsync(
            systemPartitionId,
            command,
            reader => (
                SourceCode: reader.GetString(0),
                SourceDisplay: reader.IsDBNull(1) ? null : reader.GetString(1),
                TargetCode: reader.IsDBNull(2) ? null : reader.GetString(2),
                TargetDisplay: reader.IsDBNull(3) ? null : reader.GetString(3),
                Equivalence: reader.GetString(4),
                Comment: reader.IsDBNull(5) ? null : reader.GetString(5),
                SourceSystem: reader.GetString(6),
                TargetSystem: reader.IsDBNull(7) ? null : reader.GetString(7),
                MapCanonical: reader.GetString(8)),
            cancellationToken);

        if (rows.Count == 0)
        {
            return new TranslateResult(false, "No translation found", []);
        }

        // The forward direction falls back to the literal "unknown" when a mapping has no target system or
        // code -- an unmapped element still produces a match rather than being filtered out. Preserved as-is;
        // filtering them would change which codes appear to translate.
        var matches = rows
            .Select(r =>
            {
                var (system, code, display) = parameters.Reverse
                    ? (r.SourceSystem, r.SourceCode, r.SourceDisplay)
                    : (r.TargetSystem ?? "unknown", r.TargetCode ?? "unknown", r.TargetDisplay);

                return new TranslateMatch(
                    Equivalence: r.Equivalence,
                    Concept: new TranslateConcept(system, code, display),
                    Source: r.MapCanonical,
                    Comment: r.Comment);
            })
            .ToList();

        return new TranslateResult(true, null, matches);
    }

    public async Task<SubsumesResult> SubsumesAsync(
        SubsumesParameters parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameters.CodeA);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameters.CodeB);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameters.System);

        var systemId = await ResolveSystemIdAsync(parameters.System, cancellationToken);
        if (systemId is null)
        {
            return new SubsumesResult("not-subsumed");
        }

        var versionFilter = parameters.Version is null
            ? string.Empty
            : $" AND cs.{CodeSystems.Column("Version").Name} = @version";

        using var command = Command(
            $"SELECT tc.{Concepts.Column("TermConceptId").Name}, tc.{Concepts.Column("Code").Name} " +
            $"FROM {Qualified(Concepts)} tc " +
            $"JOIN {Qualified(CodeSystems)} cs ON cs.{CodeSystems.Column("TermCodeSystemId").Name} = tc.{Concepts.Column("TermCodeSystemId").Name} " +
            $"WHERE cs.{CodeSystems.Column("SystemId").Name} = @systemId " +
            $"AND tc.{Concepts.Column("Code").Name} IN (@codeA, @codeB){versionFilter}");

        command.Parameters.AddWithValue("@systemId", systemId.Value);
        command.Parameters.AddWithValue("@codeA", parameters.CodeA);
        command.Parameters.AddWithValue("@codeB", parameters.CodeB);
        if (parameters.Version is not null)
        {
            command.Parameters.AddWithValue("@version", parameters.Version);
        }

        var concepts = await sqlExecutionService.ExecuteReaderAsync(
            systemPartitionId, command, reader => (Id: reader.GetInt64(0), Code: reader.GetString(1)), cancellationToken);

        var conceptA = concepts.FirstOrDefault(c => c.Code == parameters.CodeA);
        var conceptB = concepts.FirstOrDefault(c => c.Code == parameters.CodeB);

        if (conceptA.Code is null || conceptB.Code is null)
        {
            return new SubsumesResult("not-subsumed");
        }

        if (conceptA.Id == conceptB.Id)
        {
            return new SubsumesResult("equivalent");
        }

        if (await IsDescendantOfAsync(conceptB.Id, conceptA.Id, cancellationToken))
        {
            return new SubsumesResult("subsumes");
        }

        if (await IsDescendantOfAsync(conceptA.Id, conceptB.Id, cancellationToken))
        {
            return new SubsumesResult("subsumed-by");
        }

        return new SubsumesResult("not-subsumed");
    }

    public async Task<TerminologyImportStatus?> GetImportStatusAsync(
        string canonical, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonical);

        using var command = Command(
            $"SELECT TOP 1 {Packages.Column("TerminologyImportStatus").Name} FROM {Qualified(Packages)} " +
            $"WHERE {Packages.Column("Canonical").Name} = @canonical AND {Packages.Column("IsActive").Name} = 1 " +
            $"ORDER BY {Packages.Column("LoadedDate").Name} DESC");
        command.Parameters.AddWithValue("@canonical", canonical);

        var rows = await sqlExecutionService.ExecuteReaderAsync(
            systemPartitionId, command, reader => reader.IsDBNull(0) ? null : reader.GetString(0), cancellationToken);

        if (rows.Count == 0 || string.IsNullOrEmpty(rows[0]))
        {
            return null;
        }

        if (Enum.TryParse<TerminologyImportStatus>(rows[0], out var status))
        {
            return status;
        }

        logger.LogWarning("Invalid TerminologyImportStatus value: {Status}", rows[0]);
        return null;
    }

    /// <summary>
    /// Walks parent links upward one query per level, capped at 50, with a visited set guarding cycles.
    /// Deliberately not a recursive CTE: the cap is observable behaviour — a hierarchy deeper than 50 levels
    /// reports no relationship — and a CTE would silently answer differently past that depth.
    /// </summary>
    private async Task<bool> IsDescendantOfAsync(long descendantId, long ancestorId, CancellationToken cancellationToken)
    {
        var currentId = descendantId;
        var visited = new HashSet<long> { currentId };

        for (var depth = 0; depth < 50; depth++)
        {
            using var command = Command(
                $"SELECT {Concepts.Column("ParentConceptId").Name} FROM {Qualified(Concepts)} " +
                $"WHERE {Concepts.Column("TermConceptId").Name} = @conceptId");
            command.Parameters.AddWithValue("@conceptId", currentId);

            var rows = await sqlExecutionService.ExecuteReaderAsync(
                systemPartitionId,
                command,
                reader => reader.IsDBNull(0) ? (long?)null : reader.GetInt64(0),
                cancellationToken);

            var parentId = rows.Count > 0 ? rows[0] : null;

            if (parentId is null)
            {
                return false;
            }

            if (parentId == ancestorId)
            {
                return true;
            }

            if (!visited.Add(parentId.Value))
            {
                return false;
            }

            currentId = parentId.Value;
        }

        return false;
    }

    private async Task<int?> ResolveSystemIdAsync(string? system, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(system))
        {
            return null;
        }

        using var command = Command(
            $"SELECT TOP 1 {Systems.Column("SystemId").Name} FROM {Qualified(Systems)} " +
            $"WHERE {Systems.Column("Value").Name} = @value");
        command.Parameters.AddWithValue("@value", system);

        var rows = await sqlExecutionService.ExecuteReaderAsync(
            systemPartitionId, command, reader => reader.GetInt32(0), cancellationToken);

        return rows.Count > 0 ? rows[0] : null;
    }

    private async Task<(long Id, DateTimeOffset? LastExpansionDate, DateTimeOffset ImportedDate, bool IsPartialExpansion)?>
        ReadValueSetAsync(string canonical, CancellationToken cancellationToken)
    {
        // IsExpanded is part of the predicate, not a returned flag: a ValueSet that exists but has not been
        // expanded is indistinguishable here from one that was never imported.
        using var command = Command(
            $"SELECT TOP 1 {ValueSets.Column("TermValueSetId").Name}, {ValueSets.Column("LastExpansionDate").Name}, " +
            $"{ValueSets.Column("ImportedDate").Name}, {ValueSets.Column("IsPartialExpansion").Name} " +
            $"FROM {Qualified(ValueSets)} " +
            $"WHERE {ValueSets.Column("Canonical").Name} = @canonical AND {ValueSets.Column("IsExpanded").Name} = 1 " +
            $"ORDER BY {ValueSets.Column("ImportedDate").Name} DESC");
        command.Parameters.AddWithValue("@canonical", canonical);

        var rows = await sqlExecutionService.ExecuteReaderAsync(
            systemPartitionId,
            command,
            reader => (
                Id: reader.GetInt64(0),
                LastExpansionDate: reader.IsDBNull(1) ? (DateTimeOffset?)null : reader.GetDateTimeOffset(1),
                ImportedDate: reader.GetDateTimeOffset(2),
                IsPartialExpansion: reader.GetBoolean(3)),
            cancellationToken);

        return rows.Count > 0 ? rows[0] : null;
    }

    private async Task<int> CountExpansionAsync(
        long valueSetId, string filterClause, string? filter, CancellationToken cancellationToken)
    {
        using var command = Command(
            $"SELECT COUNT(*) FROM {Qualified(Expansions)} e " +
            $"WHERE e.{Expansions.Column("TermValueSetId").Name} = @valueSetId{filterClause}");
        command.Parameters.AddWithValue("@valueSetId", valueSetId);
        if (!string.IsNullOrWhiteSpace(filter))
        {
            command.Parameters.AddWithValue("@filter", $"%{filter}%");
        }

        var rows = await sqlExecutionService.ExecuteReaderAsync(
            systemPartitionId, command, reader => reader.GetInt32(0), cancellationToken);

        return rows.Count > 0 ? rows[0] : 0;
    }

    private static (bool IsValid, IssueSeverity Severity, string? Message) DetermineSeverityFromStrength(
        BindingStrength strength, TerminologyValidationResult codeValidation)
    {
        if (codeValidation.IsValid)
        {
            return (true, codeValidation.Severity, codeValidation.Message);
        }

        // Only required bindings fail outright; weaker strengths downgrade to a warning or information so
        // an unmapped code does not block a resource that never promised to use the ValueSet.
        return strength switch
        {
            BindingStrength.Required => (false, IssueSeverity.Error, codeValidation.Message),
            BindingStrength.Extensible => (true, IssueSeverity.Warning, codeValidation.Message),
            BindingStrength.Preferred => (true, IssueSeverity.Information, codeValidation.Message),
            _ => (true, IssueSeverity.Information, codeValidation.Message),
        };
    }

    private (IReadOnlyList<PropertyValue>?, IReadOnlyList<Designation>?) ParsePropertiesJson(string? propertiesJson)
    {
        if (string.IsNullOrEmpty(propertiesJson))
        {
            return (null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(propertiesJson);

            var properties = ReadProperties(document.RootElement);
            var designations = ReadDesignations(document.RootElement);

            return (properties, designations);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse PropertiesJson for concept");
            return (null, null);
        }
    }

    private static IReadOnlyList<PropertyValue>? ReadProperties(JsonElement root)
    {
        if (!root.TryGetProperty("property", out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var values = property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("code", out _))
            .Select(item => new PropertyValue(
                item.GetProperty("code").GetString() ?? string.Empty,
                item.TryGetProperty("value", out var value) ? value.ToString() : null))
            .ToList();

        return values.Count > 0 ? values : null;
    }

    private static IReadOnlyList<Designation>? ReadDesignations(JsonElement root)
    {
        if (!root.TryGetProperty("designation", out var designation) || designation.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var values = designation.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new Designation(
                item.TryGetProperty("language", out var language) ? language.GetString() : null,
                item.TryGetProperty("use", out var use) ? use.GetString() : null,
                item.TryGetProperty("value", out var value) ? value.GetString() ?? string.Empty : string.Empty))
            .ToList();

        return values.Count > 0 ? values : null;
    }

    private T CacheAndReturn<T>(string cacheKey, T result)
    {
        cache.Set(cacheKey, result, TimeSpan.FromMinutes(30));
        return result;
    }

    private static string Qualified(TableDescriptor table) => $"{table.SchemaName}.{table.TableName}";
}
