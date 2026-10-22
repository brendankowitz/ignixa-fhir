using System.Text.Json;
using Ignixa.Domain.Terminology;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Abstractions.Terminology;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer.Features.Terminology;

/// <summary>
/// SQL terminology source over <see cref="ISqlExecutionService"/>, replacing the EF
/// implementation. Verified against the 31-fact oracle captured in Phase F Task 5b, which recorded the EF
/// implementation's behaviour before it was replaced.
/// <para>
/// Terminology lives in the <b>system partition</b> rather than a tenant: the EF version resolved every
/// context through <c>GetDbContextAsync(SystemConstants.SystemPartitionId)</c>, so this takes the partition
/// id at construction and uses it for every query. It no longer needs the composition root — that coupling
/// is why the implementation had no test coverage before Task 5b.
/// </para>
/// <para>
/// Mutable terminology results are read from SQL on every call. Process-local memoization cannot observe
/// replacement by another process, and an in-flight old read must not publish stale results after commit.
/// A request overlapping an import can observe the prior committed state; later requests do not inherit it.
/// </para>
/// </summary>
public sealed class SqlServerTerminologyService(
    ISqlExecutionService sqlExecutionService,
    int systemPartitionId,
    ILogger<SqlServerTerminologyService> logger)
    : ITerminologySource, ILegacyValueSetOperations, ITerminologyImportStatusProvider
{
    /// <summary>
    /// Retains source compatibility for callers supplying an application cache. Mutable SQL terminology
    /// results deliberately neither read nor populate that cache.
    /// </summary>
    public SqlServerTerminologyService(
        ISqlExecutionService sqlExecutionService,
        int systemPartitionId,
        IMemoryCache cache,
        ILogger<SqlServerTerminologyService> logger)
        : this(sqlExecutionService, systemPartitionId, logger)
    {
        ArgumentNullException.ThrowIfNull(cache);
    }

    private static readonly TableDescriptor Systems = SqlCatalog.Default.Table("System");
    private static readonly TableDescriptor CodeSystems = SqlCatalog.Default.Table("TermCodeSystem");
    private static readonly TableDescriptor Concepts = SqlCatalog.Default.Table("TermConcept");
    private static readonly TableDescriptor ValueSets = SqlCatalog.Default.Table("TermValueSet");
    private static readonly TableDescriptor Expansions = SqlCatalog.Default.Table("TermValueSetExpansion");
    private static readonly TableDescriptor ConceptMaps = SqlCatalog.Default.Table("TermConceptMap");
    private static readonly TableDescriptor MapElements = SqlCatalog.Default.Table("TermConceptMapElement");

    /// <summary>
    /// The collation every code comparison below falls back to for a CodeSystem that declares
    /// <c>caseSensitive=false</c>.
    /// <para>
    /// The code columns themselves are declared <c>COLLATE Latin1_General_100_CS_AS</c> (see
    /// dbo.TermConcept, dbo.TermValueSetExpansion and dbo.TermConceptMapElement), so a plain
    /// <c>Code = @code</c> here is case-sensitive by construction rather than by whatever collation the
    /// database happens to have been created with. That is the FHIR default and the only storage that can
    /// represent both kinds of CodeSystem: under a case-insensitive collation <c>UQ_TermConcept_CodeSystem_Code</c>
    /// cannot hold both <c>AB</c> and <c>ab</c> at all, so a case-sensitive CodeSystem containing the pair is
    /// unrepresentable, while case-insensitive matching can always be layered back on at read time.
    /// </para>
    /// <para>
    /// Layering it back on is what <c>dbo.TermCodeSystem.CaseSensitive</c> is for -- the flag the importer has
    /// always written and no query had ever read. Each read path below runs its case-sensitive query first and
    /// re-runs it under this collation only on a miss, gated on <c>CaseSensitive = 0</c>. A single query
    /// covering both cases would be one round trip rather than two, but forcing a collation onto the column
    /// makes the predicate non-sargable, turning every ordinary lookup into a scan of the code index; the
    /// retry keeps the common case a seek and costs a second round trip only on a miss.
    /// </para>
    /// </summary>
    private const string CaseInsensitiveCollation = "Latin1_General_100_CI_AS";

    /// <summary>
    /// Parent links followed by one subsumption walk, one query per level. Deliberately not a recursive CTE:
    /// the bound keeps a pathological hierarchy from becoming an unbounded query. A walk that hits it answers
    /// <see cref="UnknownKind.BudgetExceeded"/>, never "not subsumed".
    /// </summary>
    private const int MaxHierarchyDepth = 50;

    // Every SQL string here is assembled from catalog identifiers and fixed literals; all caller data flows
    // through parameters. Stating the CA2100 justification once rather than at each call site.
    private static SqlCommand Command(string sql)
    {
#pragma warning disable CA2100
        return new SqlCommand(sql);
#pragma warning restore CA2100
    }

    public string Name => "sql";

    public async ValueTask<SourceCoverage> GetCoverageAsync(
        CanonicalKind kind, CanonicalRef canonical, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canonical);

        // Coverage is per kind: a ValueSet sharing this canonical says nothing about a CodeSystem of the same
        // URL. It follows usable content of that kind, not the diagnostic import status, so it agrees with
        // GetImportStatusAsync (which reports effective completion for any installed content).
        return await HasUsableContentAsync(kind, canonical.Url, canonical.Version, cancellationToken)
            ? SourceCoverage.Authoritative
            : SourceCoverage.None;
    }

    public async Task<ConceptLookup> LookupAsync(CodeRef code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);

        var systemId = await ResolveSystemIdAsync(code.System, cancellationToken);
        if (systemId is null)
        {
            return ConceptLookup.Undetermined(NotLoaded(code.System));
        }

        if (code.Version is not null
            && !await HasUsableContentAsync(
                CanonicalKind.CodeSystem, code.System, code.Version, cancellationToken))
        {
            return ConceptLookup.Undetermined(VersionNotLoaded(code.CodeSystem));
        }

        var rows = await ReadConceptAsync(
            systemId.Value, code.Code, code.Version, MatchMode.CaseSensitive, cancellationToken);
        if (rows.Count == 0)
        {
            rows = await ReadConceptAsync(
                systemId.Value, code.Code, code.Version, MatchMode.CaseInsensitiveFallback, cancellationToken);
        }

        if (rows.Count == 0)
        {
            return ConceptLookup.NotFound;
        }

        var row = rows[0];
        var (properties, designations) = ParsePropertiesJson(row.PropertiesJson);

        // Name stays null: TermCodeSystem carries no name column.
        return ConceptLookup.Found(
            new ConceptDetails(null, row.Version, row.Display, row.Definition, properties, designations));
    }

    public async Task<ExpandResult?> ExpandValueSetAsync(
        ExpansionParameters parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameters.Url);

        var valueSet = await ReadValueSetAsync(parameters.Url, cancellationToken);
        if (valueSet is null)
        {
            // Null covers both "never imported" and "imported but not expanded"; the caller cannot tell
            // them apart, which the oracle pins as current behaviour.
            logger.LogWarning("ValueSet '{Url}' not found or not expanded", parameters.Url);
            return null;
        }

        // $expand's filter is a text search for a picker, not a code identity comparison, so it stays
        // case-insensitive against both columns. Display already is; Code needs the collation forced back on
        // now that the column itself is case-sensitive, or making code *equality* correct would silently
        // narrow this to codes typed in exactly the right case.
        var filterClause = string.IsNullOrWhiteSpace(parameters.Filter)
            ? string.Empty
            : $" AND (e.{Expansions.Column("Code").Name} COLLATE {CaseInsensitiveCollation} LIKE @filter OR " +
              $"(e.{Expansions.Column("Display").Name} IS NOT NULL AND e.{Expansions.Column("Display").Name} LIKE @filter))";

        var total = await CountExpansionAsync(valueSet.Value.Id, filterClause, parameters.Filter, cancellationToken);

        var count = parameters.Count ?? 1000;
        var offset = parameters.Offset ?? 0;

        // count=0 is a deliberate FHIR $expand request for the total alone; FETCH NEXT 0 ROWS ONLY is illegal
        // T-SQL (error 10744), so the paged query is skipped entirely rather than sent with a zero fetch size.
        IReadOnlyList<ExpandedConcept> contains;
        if (count == 0)
        {
            contains = [];
        }
        else
        {
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

            contains = await sqlExecutionService.ExecuteReaderAsync(
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
        }

        var result = new ExpandResult(
            Identifier: $"urn:uuid:{Guid.NewGuid()}",
            Timestamp: valueSet.Value.LastExpansionDate ?? valueSet.Value.ImportedDate,
            Total: total,
            Offset: offset,
            Contains: contains,
            Incomplete: valueSet.Value.IsPartialExpansion);

        return result;
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

        var valueSet = await ReadValueSetAsync(valueSetUrl, cancellationToken);
        if (valueSet is null)
        {
            // Warning rather than Error: an unimported ValueSet is a deployment gap, not bad input.
            return new TerminologyValidationResult(
                false, IssueSeverity.Warning,
                $"ValueSet '{valueSetUrl}' not found or not expanded (terminology not imported)");
        }

        int? systemId = null;
        if (!string.IsNullOrEmpty(system))
        {
            systemId = await ResolveSystemIdAsync(system, cancellationToken);
            if (systemId is null)
            {
                return new TerminologyValidationResult(
                    false, IssueSeverity.Error, $"System '{system}' not found");
            }
        }

        var matches = await ReadExpansionDisplayAsync(
            valueSet.Value.Id, code, systemId, MatchMode.CaseSensitive, cancellationToken);
        if (matches.Count == 0)
        {
            matches = await ReadExpansionDisplayAsync(
                valueSet.Value.Id, code, systemId, MatchMode.CaseInsensitiveFallback, cancellationToken);
        }

        if (matches.Count == 0)
        {
            return new TerminologyValidationResult(
                false, IssueSeverity.Error, $"Code '{code}' not found in ValueSet '{valueSetUrl}'");
        }

        var expectedDisplay = matches[0];

        // A display mismatch is a warning on a *valid* code, not an invalid one.
        if (!string.IsNullOrEmpty(display)
            && !string.IsNullOrEmpty(expectedDisplay)
            && !string.Equals(display, expectedDisplay, StringComparison.Ordinal))
        {
            return new TerminologyValidationResult(
                true, IssueSeverity.Warning, $"Display '{display}' does not match expected '{expectedDisplay}'");
        }

        return new TerminologyValidationResult(true, IssueSeverity.Information, "Code is valid");
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

        // A non-null blank version filtered the legacy concept read on the blank string, which matched no
        // concept, so no display was suggested. CodeRef would normalise it to "unversioned" instead.
        var blankVersion = version is not null && string.IsNullOrWhiteSpace(version);

        if (codeValidation.IsValid
            && !blankVersion
            && !string.IsNullOrEmpty(display)
            && !string.IsNullOrEmpty(system)
            && !string.IsNullOrEmpty(code))
        {
            var lookup = await LookupAsync(new CodeRef(system, code, version), cancellationToken);

            if (lookup.Concept is { Display: { } expectedDisplay }
                && !string.Equals(display, expectedDisplay, StringComparison.Ordinal))
            {
                suggestedDisplay = expectedDisplay;
                if (severity < IssueSeverity.Warning)
                {
                    severity = IssueSeverity.Warning;
                }

                message = $"{message ?? "Code is valid"} However, display '{display}' does not match expected '{expectedDisplay}'";
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

        // A caller-supplied url scopes the translation to that one ConceptMap (and its version, when given);
        // omitting url preserves the legitimate "search across every imported ConceptMap" behaviour.
        var mapFilter = string.Empty;
        if (!string.IsNullOrEmpty(parameters.Url))
        {
            mapFilter = $" AND cm.{maps.Column("Canonical").Name} = @url";

            if (parameters.ConceptMapVersion is not null)
            {
                mapFilter += $" AND cm.{maps.Column("Version").Name} = @conceptMapVersion";
            }
        }

        var rows = await ReadTranslationsAsync(MatchMode.CaseSensitive);
        if (rows.Count == 0)
        {
            rows = await ReadTranslationsAsync(MatchMode.CaseInsensitiveFallback);
        }

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

        async Task<IReadOnlyList<(string SourceCode, string? SourceDisplay, string? TargetCode, string? TargetDisplay,
            string Equivalence, string? Comment, string SourceSystem, string? TargetSystem, string MapCanonical)>>
            ReadTranslationsAsync(MatchMode mode)
        {
            var codeMatch = mode == MatchMode.CaseSensitive
                ? $"e.{MapElements.Column(codeColumn).Name} = @code"
                : $"e.{MapElements.Column(codeColumn).Name} COLLATE {CaseInsensitiveCollation} = @code";

            var caseSensitiveGuard = mode == MatchMode.CaseSensitive
                ? string.Empty
                : CaseInsensitiveSystemGuard($"e.{MapElements.Column(matchColumn).Name}");

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
                $"AND {codeMatch}{targetFilter}{mapFilter}{caseSensitiveGuard}");

            command.Parameters.AddWithValue("@sourceSystemId", sourceSystemId.Value);
            command.Parameters.AddWithValue("@code", parameters.Code);
            if (targetSystemId is not null && targetFilter.Length > 0)
            {
                command.Parameters.AddWithValue("@targetSystemId", targetSystemId.Value);
            }

            if (!string.IsNullOrEmpty(parameters.Url))
            {
                command.Parameters.AddWithValue("@url", parameters.Url);
                if (parameters.ConceptMapVersion is not null)
                {
                    command.Parameters.AddWithValue("@conceptMapVersion", parameters.ConceptMapVersion);
                }
            }

            return await sqlExecutionService.ExecuteReaderAsync(
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
        }
    }

    public async Task<SubsumesOutcome> SubsumesAsync(
        CodeRef codeA, CodeRef codeB, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(codeA);
        ArgumentNullException.ThrowIfNull(codeB);

        if (!string.Equals(codeA.System, codeB.System, StringComparison.Ordinal))
        {
            return new SubsumesOutcome.Undetermined(NotLoaded(codeB.System));
        }

        if (!string.Equals(codeA.Version, codeB.Version, StringComparison.Ordinal))
        {
            return new SubsumesOutcome.Undetermined(VersionNotLoaded(codeB.CodeSystem));
        }

        var systemId = await ResolveSystemIdAsync(codeA.System, cancellationToken);
        if (systemId is null)
        {
            return new SubsumesOutcome.Undetermined(NotLoaded(codeA.System));
        }

        if (codeA.Version is not null
            && !await HasUsableContentAsync(
                CanonicalKind.CodeSystem, codeA.System, codeA.Version, cancellationToken))
        {
            return new SubsumesOutcome.Undetermined(VersionNotLoaded(codeA.CodeSystem));
        }

        // One resolution per code, each matching exactly the way the other read paths do. See
        // FindConceptIdAsync for why this is not the single IN (@codeA, @codeB) query it replaces.
        var conceptA = await FindConceptIdAsync(systemId.Value, codeA.Code, codeA.Version, cancellationToken);
        if (conceptA is null)
        {
            return new SubsumesOutcome.Undetermined(NotInSystem(codeA));
        }

        var conceptB = await FindConceptIdAsync(systemId.Value, codeB.Code, codeB.Version, cancellationToken);
        if (conceptB is null)
        {
            return new SubsumesOutcome.Undetermined(NotInSystem(codeB));
        }

        // Two codes differing only by case in a caseSensitive=false CodeSystem resolve to the same concept,
        // and "equivalent" is the right answer for them.
        if (conceptA == conceptB)
        {
            return new SubsumesOutcome.Related(SubsumptionRelation.Equivalent);
        }

        var bUnderA = await WalkAncestryAsync(conceptB.Value, conceptA.Value, cancellationToken);
        if (bUnderA == Ancestry.Descendant)
        {
            return new SubsumesOutcome.Related(SubsumptionRelation.Subsumes);
        }

        var aUnderB = await WalkAncestryAsync(conceptA.Value, conceptB.Value, cancellationToken);
        if (aUnderB == Ancestry.Descendant)
        {
            return new SubsumesOutcome.Related(SubsumptionRelation.SubsumedBy);
        }

        // "Not subsumed" is only provable when both walks reached a root. A walk cut short by the depth
        // budget or a parent cycle proves nothing, so it stays Undetermined rather than becoming false.
        var unknowns = new List<UnknownReason>(2);
        AddWalkUnknown(unknowns, bUnderA, codeB, codeA);
        AddWalkUnknown(unknowns, aUnderB, codeA, codeB);

        return unknowns.Count == 0
            ? new SubsumesOutcome.Related(SubsumptionRelation.NotSubsumed)
            : new SubsumesOutcome.Undetermined(unknowns);
    }

    private static void AddWalkUnknown(List<UnknownReason> unknowns, Ancestry walk, CodeRef descendant, CodeRef ancestor)
    {
        switch (walk)
        {
            case Ancestry.DepthExceeded:
                unknowns.Add(new UnknownReason(
                    UnknownKind.BudgetExceeded,
                    descendant.ToString(),
                    $"Hierarchy walk from '{descendant.Code}' toward '{ancestor.Code}' exceeded {MaxHierarchyDepth} levels"));
                break;
            case Ancestry.Cycle:
                unknowns.Add(new UnknownReason(
                    UnknownKind.SourceFailed,
                    descendant.ToString(),
                    $"Hierarchy walk from '{descendant.Code}' toward '{ancestor.Code}' found a parent cycle in '{descendant.CodeSystem}'"));
                break;
        }
    }

    public async Task<TerminologyImportStatus?> GetImportStatusAsync(
        string canonical, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonical);

        // Routing reflects usable installed content, not the latest replacement attempt. Its package row
        // still records Failed for diagnostics/retry, but rollback must not send hybrid lookups to fallback.
        using var command = Command("""
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM dbo.PackageResource p
                WHERE p.Canonical = @canonical AND p.IsActive = 1
                  AND (EXISTS (SELECT 1 FROM dbo.TermCodeSystem cs WHERE cs.PackageResourceId = p.PackageResourceId)
                    OR EXISTS (SELECT 1 FROM dbo.TermValueSet vs WHERE vs.PackageResourceId = p.PackageResourceId AND vs.IsExpanded = 1)
                    OR EXISTS (SELECT 1 FROM dbo.TermConceptMap cm WHERE cm.PackageResourceId = p.PackageResourceId))
            ) THEN 'Completed' ELSE (
                SELECT TOP 1 TerminologyImportStatus FROM dbo.PackageResource
                WHERE Canonical = @canonical AND IsActive = 1
                ORDER BY LoadedDate DESC, PackageResourceId DESC
            ) END
            """);
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

    private static UnknownReason NotLoaded(string system) =>
        new(UnknownKind.SystemNotAvailable, system, "CodeSystem is not loaded in the SQL terminology store");

    private static UnknownReason VersionNotLoaded(CanonicalRef codeSystem) =>
        new(UnknownKind.SystemVersionNotAvailable, codeSystem.ToString(),
            "CodeSystem version is not loaded in the SQL terminology store");

    private static UnknownReason NotInSystem(CodeRef code) =>
        new(UnknownKind.CodeNotInSystem, code.ToString(), $"Code '{code.Code}' is not defined in '{code.CodeSystem}'");

    /// <summary>
    /// Whether an active package row for <paramref name="canonical"/> carries usable content of exactly
    /// <paramref name="kind"/> — for a ValueSet, an expansion — restricted to <paramref name="version"/>
    /// when one is given. The row's <c>TerminologyImportStatus</c> is diagnostic and deliberately ignored:
    /// content a rolled-back replacement left behind under a <c>Failed</c> (or null) row is still served, so
    /// coverage, reads and <see cref="GetImportStatusAsync"/> all follow the content itself.
    /// </summary>
    private async Task<bool> HasUsableContentAsync(
        CanonicalKind kind, string canonical, string? version, CancellationToken cancellationToken)
    {
        var resource = kind switch
        {
            CanonicalKind.CodeSystem => CodeSystems,
            CanonicalKind.ValueSet => ValueSets,
            CanonicalKind.ConceptMap => ConceptMaps,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        var usableContentPredicate = kind switch
        {
            CanonicalKind.CodeSystem =>
                $" AND EXISTS (SELECT 1 FROM {Qualified(Concepts)} c " +
                $"WHERE c.{Concepts.Column("TermCodeSystemId").Name} = " +
                $"r.{CodeSystems.Column("TermCodeSystemId").Name})",
            CanonicalKind.ValueSet => $" AND r.{resource.Column("IsExpanded").Name} = 1",
            _ => string.Empty,
        };
        var versionPredicate = version is null
            ? string.Empty
            : $" AND r.{resource.Column("Version").Name} = @version";

        using var command = Command(
            $"SELECT CASE WHEN EXISTS (" +
            $"SELECT 1 FROM dbo.PackageResource p " +
            $"JOIN {Qualified(resource)} r ON r.{resource.Column("PackageResourceId").Name} = p.PackageResourceId " +
            $"WHERE p.Canonical = @canonical AND p.IsActive = 1" +
            $"{versionPredicate}{usableContentPredicate}" +
            $") THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END");
        command.Parameters.AddWithValue("@canonical", canonical);
        if (version is not null)
        {
            command.Parameters.AddWithValue("@version", version);
        }

        var rows = await sqlExecutionService.ExecuteReaderAsync(
            systemPartitionId, command, reader => reader.GetBoolean(0), cancellationToken);

        return rows.Count > 0 && rows[0];
    }

    /// <summary>
    /// Walks parent links upward one query per level, capped at <see cref="MaxHierarchyDepth"/>, with a
    /// visited set guarding cycles. Only reaching a root proves <see cref="Ancestry.NotDescendant"/>; a walk
    /// stopped by the cap or by a cycle reports that instead, so the caller can answer Unknown rather than
    /// "not subsumed" for an ancestor it simply did not reach.
    /// </summary>
    private async Task<Ancestry> WalkAncestryAsync(long descendantId, long ancestorId, CancellationToken cancellationToken)
    {
        var currentId = descendantId;
        var visited = new HashSet<long> { currentId };

        for (var depth = 0; depth < MaxHierarchyDepth; depth++)
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
                return Ancestry.NotDescendant;
            }

            if (parentId == ancestorId)
            {
                return Ancestry.Descendant;
            }

            if (!visited.Add(parentId.Value))
            {
                return Ancestry.Cycle;
            }

            currentId = parentId.Value;
        }

        return Ancestry.DepthExceeded;
    }

    private enum Ancestry
    {
        Descendant,
        NotDescendant,
        DepthExceeded,
        Cycle,
    }

    /// <summary>
    /// The display recorded for <paramref name="code"/> in an expansion, or no rows when the code is not in
    /// it. The expansion table carries a SystemId but no TermCodeSystemId, so the case-insensitive fallback
    /// reaches its CodeSystem through <see cref="CaseInsensitiveSystemGuard"/> — against the expansion row's
    /// own system when the caller named none.
    /// </summary>
    private async Task<IReadOnlyList<string?>> ReadExpansionDisplayAsync(
        long valueSetId, string code, int? systemId, MatchMode mode, CancellationToken cancellationToken)
    {
        var systemFilter = systemId is null
            ? string.Empty
            : $" AND e.{Expansions.Column("SystemId").Name} = @systemId";

        var codeMatch = mode == MatchMode.CaseSensitive
            ? $"e.{Expansions.Column("Code").Name} = @code"
            : $"e.{Expansions.Column("Code").Name} COLLATE {CaseInsensitiveCollation} = @code";

        var caseSensitiveGuard = mode == MatchMode.CaseSensitive
            ? string.Empty
            : CaseInsensitiveSystemGuard($"e.{Expansions.Column("SystemId").Name}");

        using var command = Command(
            $"SELECT TOP 1 e.{Expansions.Column("Display").Name} FROM {Qualified(Expansions)} e " +
            $"WHERE e.{Expansions.Column("TermValueSetId").Name} = @valueSetId " +
            $"AND {codeMatch}{systemFilter}{caseSensitiveGuard}");
        command.Parameters.AddWithValue("@valueSetId", valueSetId);
        command.Parameters.AddWithValue("@code", code);
        if (systemId is not null)
        {
            command.Parameters.AddWithValue("@systemId", systemId.Value);
        }

        return await sqlExecutionService.ExecuteReaderAsync(
            systemPartitionId, command, reader => reader.IsDBNull(0) ? null : reader.GetString(0), cancellationToken);
    }

    /// <summary>
    /// Which comparison a code predicate is being built for. <see cref="CaseInsensitiveFallback"/> relaxes
    /// the collation <em>and</em> adds the <c>CaseSensitive = 0</c> guard; the two belong together, because a
    /// relaxed collation without the guard is a blanket case-insensitive match — the defect being fixed.
    /// </summary>
    private enum MatchMode
    {
        CaseSensitive,
        CaseInsensitiveFallback,
    }

    /// <summary>
    /// Matches <c>tc.Code</c> against <c>@code</c> for a query that has already joined the concept's
    /// CodeSystem as <c>cs</c>, so the <c>caseSensitive=false</c> guard reads a column on that row.
    /// </summary>
    private static string ConceptCodeMatch(MatchMode mode) => mode == MatchMode.CaseSensitive
        ? $"tc.{Concepts.Column("Code").Name} = @code"
        : $"tc.{Concepts.Column("Code").Name} COLLATE {CaseInsensitiveCollation} = @code " +
          $"AND cs.{CodeSystems.Column("CaseSensitive").Name} = 0";

    /// <summary>
    /// The <c>caseSensitive=false</c> guard for a query that has <em>not</em> joined a CodeSystem — the
    /// expansion and concept-map tables carry a SystemId but no TermCodeSystemId. Written as EXISTS rather
    /// than a join so a system with several imported CodeSystem versions cannot multiply the rows.
    /// </summary>
    private static string CaseInsensitiveSystemGuard(string systemIdExpression)
        => $" AND EXISTS (SELECT 1 FROM {Qualified(CodeSystems)} cs " +
           $"WHERE cs.{CodeSystems.Column("SystemId").Name} = {systemIdExpression} " +
           $"AND cs.{CodeSystems.Column("CaseSensitive").Name} = 0)";

    /// <summary>
    /// The most recently imported concept for <paramref name="code"/> in <paramref name="systemId"/>, or no
    /// rows. Ordered by the code system's import date so the most recently imported version wins when a
    /// concept appears in more than one, matching the EF query's <c>OrderByDescending(ImportedDate)</c>.
    /// </summary>
    private async Task<IReadOnlyList<(string? Display, string? Definition, string? PropertiesJson, string? Version)>>
        ReadConceptAsync(int systemId, string code, string? version, MatchMode mode, CancellationToken cancellationToken)
    {
        var versionFilter = version is null
            ? string.Empty
            : $" AND cs.{CodeSystems.Column("Version").Name} = @version";

        using var command = Command(
            $"SELECT TOP 1 tc.{Concepts.Column("Display").Name}, tc.{Concepts.Column("Definition").Name}, " +
            $"tc.{Concepts.Column("PropertiesJson").Name}, cs.{CodeSystems.Column("Version").Name} " +
            $"FROM {Qualified(Concepts)} tc " +
            $"JOIN {Qualified(CodeSystems)} cs ON cs.{CodeSystems.Column("TermCodeSystemId").Name} = tc.{Concepts.Column("TermCodeSystemId").Name} " +
            $"WHERE cs.{CodeSystems.Column("SystemId").Name} = @systemId AND {ConceptCodeMatch(mode)}" +
            versionFilter +
            $" ORDER BY cs.{CodeSystems.Column("ImportedDate").Name} DESC");

        command.Parameters.AddWithValue("@systemId", systemId);
        command.Parameters.AddWithValue("@code", code);
        if (version is not null)
        {
            command.Parameters.AddWithValue("@version", version);
        }

        return await sqlExecutionService.ExecuteReaderAsync(
            systemPartitionId,
            command,
            reader => (
                Display: reader.IsDBNull(0) ? null : reader.GetString(0),
                Definition: reader.IsDBNull(1) ? null : reader.GetString(1),
                PropertiesJson: reader.IsDBNull(2) ? null : reader.GetString(2),
                Version: reader.IsDBNull(3) ? null : reader.GetString(3)),
            cancellationToken);
    }

    /// <summary>
    /// The identity of the concept <paramref name="code"/> resolves to, case-sensitively first and then
    /// under <see cref="CaseInsensitiveCollation"/> if its CodeSystem declares <c>caseSensitive=false</c>.
    /// <para>
    /// <see cref="SubsumesAsync"/> resolves each of its two codes through this rather than fetching both in
    /// one <c>Code IN (@codeA, @codeB)</c> query and splitting the result in C#. That split compared the
    /// returned codes with <c>==</c> — ordinal — while the query that returned them compared under the
    /// database's collation, so the two halves of one operation disagreed about what equality means: a row
    /// SQL had matched could be rejected by the C# predicate, leaving the concept unresolved and the
    /// operation answering "not-subsumed" with no indication anything had gone wrong. There is no second
    /// comparison here to disagree with the first.
    /// </para>
    /// </summary>
    private async Task<long?> FindConceptIdAsync(
        int systemId, string code, string? version, CancellationToken cancellationToken)
    {
        var id = await ReadConceptIdAsync(MatchMode.CaseSensitive);
        return id ?? await ReadConceptIdAsync(MatchMode.CaseInsensitiveFallback);

        async Task<long?> ReadConceptIdAsync(MatchMode mode)
        {
            var versionFilter = version is null
                ? string.Empty
                : $" AND cs.{CodeSystems.Column("Version").Name} = @version";

            using var command = Command(
                $"SELECT TOP 1 tc.{Concepts.Column("TermConceptId").Name} " +
                $"FROM {Qualified(Concepts)} tc " +
                $"JOIN {Qualified(CodeSystems)} cs ON cs.{CodeSystems.Column("TermCodeSystemId").Name} = tc.{Concepts.Column("TermCodeSystemId").Name} " +
                $"WHERE cs.{CodeSystems.Column("SystemId").Name} = @systemId AND {ConceptCodeMatch(mode)}" +
                versionFilter +
                $" ORDER BY cs.{CodeSystems.Column("ImportedDate").Name} DESC");

            command.Parameters.AddWithValue("@systemId", systemId);
            command.Parameters.AddWithValue("@code", code);
            if (version is not null)
            {
                command.Parameters.AddWithValue("@version", version);
            }

            var rows = await sqlExecutionService.ExecuteReaderAsync(
                systemPartitionId, command, reader => reader.GetInt64(0), cancellationToken);

            return rows.Count > 0 ? rows[0] : null;
        }
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

    /// <summary>
    /// Internal rather than private so <c>Ignixa.DataLayer.SqlServer.Tests</c> can exercise the
    /// concept-property/designation JSON parsing directly, without a database.
    /// </summary>
    internal (IReadOnlyList<PropertyValue>?, IReadOnlyList<Designation>?) ParsePropertiesJson(string? propertiesJson)
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
                ReadPropertyValue(item)))
            .ToList();

        return values.Count > 0 ? values : null;
    }

    /// <summary>
    /// FHIR represents <c>CodeSystem.concept.property.value[x]</c> with a type-suffixed key rather than a
    /// literal "value" key; this mirrors the typed-key lookup in
    /// <see cref="SqlServerValueSetComposer"/>'s ReadPropertyValues.
    /// </summary>
    private static string? ReadPropertyValue(JsonElement item)
    {
        if (item.TryGetProperty("valueCode", out var valueCode) && valueCode.ValueKind == JsonValueKind.String)
        {
            return valueCode.GetString();
        }

        if (item.TryGetProperty("valueString", out var valueString) && valueString.ValueKind == JsonValueKind.String)
        {
            return valueString.GetString();
        }

        if (item.TryGetProperty("valueDateTime", out var valueDateTime) && valueDateTime.ValueKind == JsonValueKind.String)
        {
            return valueDateTime.GetString();
        }

        if (item.TryGetProperty("valueBoolean", out var valueBoolean)
            && valueBoolean.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return valueBoolean.GetBoolean() ? "true" : "false";
        }

        if (item.TryGetProperty("valueInteger", out var valueInteger) && valueInteger.ValueKind == JsonValueKind.Number)
        {
            return valueInteger.ToString();
        }

        if (item.TryGetProperty("valueDecimal", out var valueDecimal) && valueDecimal.ValueKind == JsonValueKind.Number)
        {
            return valueDecimal.ToString();
        }

        if (item.TryGetProperty("valueCoding", out var valueCoding) && valueCoding.ValueKind == JsonValueKind.Object
            && valueCoding.TryGetProperty("code", out var codingCode) && codingCode.ValueKind == JsonValueKind.String)
        {
            return codingCode.GetString();
        }

        return null;
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
                item.TryGetProperty("language", out var language) && language.ValueKind == JsonValueKind.String
                    ? language.GetString()
                    : null,
                item.TryGetProperty("use", out var use) && use.ValueKind == JsonValueKind.Object
                    && use.TryGetProperty("code", out var useCode) && useCode.ValueKind == JsonValueKind.String
                        ? useCode.GetString()
                        : null,
                item.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? string.Empty
                    : string.Empty))
            .ToList();

        return values.Count > 0 ? values : null;
    }

    private static string Qualified(TableDescriptor table) => $"{table.SchemaName}.{table.TableName}";
}
