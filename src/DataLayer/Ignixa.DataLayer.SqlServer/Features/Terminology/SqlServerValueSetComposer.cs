using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Terminology;
using Ignixa.Search.Sql.Catalog;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer.Features.Terminology;

/// <summary>
/// Resolves a <c>ValueSet.compose</c> into the set of codes it designates, reading concepts and previously
/// expanded ValueSets through <see cref="ISqlExecutionService"/>.
/// <para>
/// Single use by construction: one instance accumulates one expansion, which is why the entry point is
/// <see cref="ComposeAsync"/> rather than a constructor. Includes are processed before excludes because
/// exclusion is applied to the included set, not to the query that produced it.
/// </para>
/// <para>
/// <b>Three defects in the implementation this replaces are fixed here rather than reproduced.</b>
/// </para>
/// <para>
/// <b>Excludes were evaluated by a different, weaker filter path than includes.</b> It understood only
/// <c>code =</c>, <c>code in</c>, <c>display =</c> and <c>display contains</c>, and any other filter fell
/// through a <c>switch</c> that left the query unrestricted — so <c>exclude</c> with, say, an <c>is-a</c>
/// filter selected <b>every code in the system</b> and removed them all. Both directions now share one
/// evaluator, so a filter means the same thing whichever side of the compose it appears on.
/// </para>
/// <para>
/// <b>An unsupported filter operator matched everything.</b> On the include side <c>op</c> values outside the
/// handled set returned true per concept, quietly turning a narrow filter into "the whole CodeSystem". An
/// operator this type cannot evaluate now matches nothing and marks the expansion partial, so the gap is
/// visible in <c>PartialExpansionReason</c> instead of showing up as codes that were never asked for.
/// </para>
/// <para>
/// <b><c>descendent-of</c> was treated as a synonym for <c>is-a</c>.</b> They differ by exactly one concept:
/// <c>is-a</c> includes the named code, <c>descendent-of</c> does not.
/// </para>
/// <para>
/// A fourth defect was fixed upstream: ancestry is walked over <c>ParentConceptId</c>, which
/// <c>dbo.ImportTermCodeSystem</c> now populates for every CodeSystem. Before that, any CodeSystem of 1,000
/// concepts or fewer imported flat, and every <c>is-a</c> filter over one silently resolved to nothing.
/// </para>
/// </summary>
internal sealed class SqlServerValueSetComposer
{
    private const int PartialReasonMaxLength = 1024;

    private static readonly TableDescriptor CodeSystems = SqlCatalog.Default.Table("TermCodeSystem");
    private static readonly TableDescriptor Concepts = SqlCatalog.Default.Table("TermConcept");
    private static readonly TableDescriptor ValueSets = SqlCatalog.Default.Table("TermValueSet");
    private static readonly TableDescriptor Expansions = SqlCatalog.Default.Table("TermValueSetExpansion");

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    // Buckets find possible case variants; actual matches still use the system/version policy.
    private static readonly IEqualityComparer<(int SystemId, string Code)> CandidateCodeComparer =
        EqualityComparer<(int SystemId, string Code)>.Create(
            (left, right) => left.SystemId == right.SystemId && StringComparer.OrdinalIgnoreCase.Equals(left.Code, right.Code),
            key => HashCode.Combine(key.SystemId, StringComparer.OrdinalIgnoreCase.GetHashCode(key.Code)));

    private readonly ISqlExecutionService _sqlExecutionService;
    private readonly int _systemPartitionId;
    private readonly ISystemRepository _systemRepository;
    private readonly ILogger _logger;
    private readonly int _commandTimeoutSeconds;

    private readonly List<ValueSetExpansionRow> _included = [];
    private readonly Dictionary<(int SystemId, string Code), List<ValueSetExpansionRow>> _includedByCode = new(CandidateCodeComparer);
    private readonly Dictionary<(int SystemId, string? Version), bool> _caseSensitivityByVersion = [];
    private readonly Dictionary<int, bool?> _caseSensitivityBySystem = [];
    private readonly List<string> _externalSystems = [];
    private readonly List<string> _missingValueSets = [];
    private readonly Dictionary<string, string?> _partialValueSets = new(StringComparer.Ordinal);
    private readonly List<string> _unknownSystemVersions = [];
    private readonly List<string> _unsupportedFilters = [];

    private SqlServerValueSetComposer(
        ISqlExecutionService sqlExecutionService,
        int systemPartitionId,
        ISystemRepository systemRepository,
        ILogger logger,
        int commandTimeoutSeconds)
    {
        _sqlExecutionService = sqlExecutionService;
        _systemPartitionId = systemPartitionId;
        _systemRepository = systemRepository;
        _logger = logger;
        _commandTimeoutSeconds = commandTimeoutSeconds > 0
            ? commandTimeoutSeconds
            : throw new ArgumentOutOfRangeException(
                nameof(commandTimeoutSeconds), commandTimeoutSeconds, "Command timeout must be positive.");
    }

    /// <param name="commandTimeoutSeconds">
    /// <see cref="SqlCommand.CommandTimeout"/> for every command this composer issues. Shares
    /// <see cref="SqlServerOptions.TerminologyImportCommandTimeoutSeconds"/> with the CodeSystem/ValueSet/
    /// ConceptMap import procedures: <see cref="ReadConceptsAsync"/> in particular runs an unbounded
    /// "every concept in this system" read for a plain <c>compose.include.system</c> with no <c>concept</c>
    /// or <c>filter</c> array, which for a SNOMED-scale include reads as many rows as the import itself
    /// writes -- and it ran BEFORE the configurable timeout reached this class, still on ADO's 30-second
    /// default, regardless of how the importer's own commands were configured.
    /// </param>
    public static Task<ComposedExpansion> ComposeAsync(
        JsonObject compose,
        ISqlExecutionService sqlExecutionService,
        int systemPartitionId,
        ISystemRepository systemRepository,
        ILogger logger,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
        => new SqlServerValueSetComposer(sqlExecutionService, systemPartitionId, systemRepository, logger, commandTimeoutSeconds)
            .RunAsync(compose, cancellationToken);

    private async Task<ComposedExpansion> RunAsync(JsonObject compose, CancellationToken cancellationToken)
    {
        await ReadCodeComparisonPoliciesAsync(cancellationToken);
        foreach (var include in ObjectsOf(compose["include"]))
        {
            await ProcessClauseAsync(include, isExclude: false, cancellationToken);
        }

        foreach (var exclude in ObjectsOf(compose["exclude"]))
        {
            await ProcessClauseAsync(exclude, isExclude: true, cancellationToken);
        }

        var isPartial = _externalSystems.Count > 0 || _missingValueSets.Count > 0 || _unsupportedFilters.Count > 0
            || _partialValueSets.Count > 0 || _unknownSystemVersions.Count > 0;

        return new ComposedExpansion(_included, isPartial, isPartial ? BuildPartialReason() : null);
    }

    /// <summary>
    /// Intersects the conditions within one clause before unioning an include or subtracting an exclude.
    /// </summary>
    private async Task ProcessClauseAsync(JsonObject clause, bool isExclude, CancellationToken cancellationToken)
    {
        var system = clause["system"]?.GetValue<string>();
        var version = clause["version"]?.GetValue<string>();
        if (version == "*")
        {
            version = null;
        }
        var concepts = clause["concept"] as JsonArray;
        var valueSets = CanonicalsOf(clause["valueSet"] as JsonArray).ToArray();
        var filters = clause["filter"] as JsonArray;

        if (concepts is { Count: > 0 } && filters is { Count: > 0 })
        {
            throw new InvalidOperationException("ValueSet compose clauses cannot have both concept and filter (vsd-3).");
        }
        if (system is null && (concepts is { Count: > 0 } || filters is { Count: > 0 }))
        {
            throw new InvalidOperationException("ValueSet compose clauses with concepts or filters require a system (vsd-2).");
        }
        if (system is null && valueSets.Length == 0)
        {
            throw new InvalidOperationException("ValueSet compose clauses require a system or a ValueSet reference (vsd-1).");
        }

        List<ValueSetExpansionRow>? selected = null;
        foreach (var canonical in valueSets)
        {
            var rows = await ReadReferencedValueSetAsync(canonical, cancellationToken);
            selected = selected is null ? rows.ToList() : Intersect(selected, rows);
        }

        if (system is not null)
        {
            selected = await SelectSystemCodesAsync(system, version, concepts, filters, selected, isExclude, cancellationToken);
        }

        var selectedRows = selected ?? throw new InvalidOperationException("A compose clause did not produce a selection.");
        if (isExclude)
        {
            var byCode = selectedRows.ToLookup(row => (row.SystemId, row.Code), CandidateCodeComparer);
            _included.RemoveAll(row => byCode[(row.SystemId, row.Code)].Any(excluded => CanExclude(row, excluded)));
            return;
        }
        foreach (var row in selectedRows)
        {
            if (AddDistinct(_includedByCode, row))
            {
                _included.Add(row);
            }
        }
    }

    private async Task<List<ValueSetExpansionRow>> SelectSystemCodesAsync(
        string system, string? version, JsonArray? concepts, JsonArray? filters,
        List<ValueSetExpansionRow>? referenced, bool isExclude, CancellationToken cancellationToken)
    {
        var systemId = await ResolveSystemIdAsync(system, isExclude, cancellationToken);
        if (systemId is null)
        {
            return [];
        }
        if (version is not null
            && (referenced?.Any(row => row.SystemId == systemId.Value && row.SystemVersion is null) == true
                || isExclude && _included.Any(row => row.SystemId == systemId.Value && row.SystemVersion is null)))
        {
            TrackUnknownSystemVersion($"{system}|{version}");
        }
        if (referenced is not null)
        {
            referenced = referenced.Where(row => row.SystemId == systemId.Value
                && (version is null || string.Equals(version, row.SystemVersion, StringComparison.Ordinal))).ToList();
        }

        if (concepts is { Count: > 0 })
        {
            var rows = ObjectsOf(concepts)
                .Where(concept => !string.IsNullOrEmpty(concept["code"]?.GetValue<string>()))
                .Select(concept => new ValueSetExpansionRow(systemId.Value, concept["code"]!.GetValue<string>(),
                    concept["display"]?.GetValue<string>(), version)).ToList();
            return referenced is null ? rows : SelectExplicitConcepts(rows, referenced);
        }
        if (filters is { Count: > 0 })
        {
            var rows = await ReadFilteredSystemCodesAsync(systemId.Value, system, version, filters, isExclude, cancellationToken);
            return referenced is null ? rows : Intersect(referenced, rows);
        }
        if (referenced is not null)
        {
            return referenced;
        }
        if (isExclude)
        {
            // Whole-system exclusions operate on included rows, without reading CodeSystem content.
            return _included.Where(row => row.SystemId == systemId.Value
                && (version is null || string.Equals(version, row.SystemVersion, StringComparison.Ordinal))).ToList();
        }

        var candidates = await ReadConceptsAsync(systemId.Value, version, cancellationToken);
        if (candidates.Count == 0)
        {
            TrackExternalSystem(system);
        }
        return candidates.Select(concept => new ValueSetExpansionRow(
            systemId.Value, concept.Code, concept.Display, concept.Version)).ToList();
    }

    private List<ValueSetExpansionRow> Intersect(
        IReadOnlyList<ValueSetExpansionRow> left, IReadOnlyList<ValueSetExpansionRow> right)
    {
        var byCode = right.ToLookup(row => (row.SystemId, row.Code), CandidateCodeComparer);
        return DistinctRows(left.SelectMany(row => byCode[(row.SystemId, row.Code)]
                .Where(other => CanIntersect(row, other))
                .Select(other => row with
                {
                    Display = row.Display ?? other.Display,
                })));
    }

    private List<ValueSetExpansionRow> SelectExplicitConcepts(
        IReadOnlyList<ValueSetExpansionRow> concepts, IReadOnlyList<ValueSetExpansionRow> referenced)
    {
        var byCode = referenced.ToLookup(row => (row.SystemId, row.Code), CandidateCodeComparer);
        // An unqualified concept is a code predicate, not evidence of an expansion's unknown version.
        return DistinctRows(concepts.SelectMany(concept => byCode[(concept.SystemId, concept.Code)]
            .Where(row => CodesMatch(concept, row, row.SystemVersion))
            .Select(row => new ValueSetExpansionRow(
                row.SystemId, concept.Code, concept.Display ?? row.Display, row.SystemVersion))));
    }

    private bool CanIntersect(ValueSetExpansionRow left, ValueSetExpansionRow right)
    {
        if (left.SystemVersion is not null && right.SystemVersion is not null
            && !string.Equals(left.SystemVersion, right.SystemVersion, StringComparison.Ordinal))
        {
            return false;
        }
        var version = left.SystemVersion ?? right.SystemVersion;
        if (!CodesMatch(left, right, version))
        {
            return false;
        }
        if (string.Equals(left.SystemVersion, right.SystemVersion, StringComparison.Ordinal))
        {
            return true;
        }
        TrackUnknownSystemVersion($"system {left.SystemId}|{version}");
        return false;
    }

    private bool CanExclude(ValueSetExpansionRow included, ValueSetExpansionRow excluded)
    {
        if (included.SystemVersion is not null && excluded.SystemVersion is not null
            && !string.Equals(included.SystemVersion, excluded.SystemVersion, StringComparison.Ordinal))
        {
            return false;
        }
        if (!CodesMatch(included, excluded, excluded.SystemVersion ?? included.SystemVersion))
        {
            return false;
        }
        if (included.SystemVersion is null && excluded.SystemVersion is not null)
        {
            // An unqualified exclusion is unrestricted; a pinned exclusion cannot resolve a missing version.
            TrackUnknownSystemVersion($"system {included.SystemId}|{excluded.SystemVersion}");
            return false;
        }
        return true;
    }

    private bool CodesMatch(ValueSetExpansionRow left, ValueSetExpansionRow right, string? version)
    {
        if (string.Equals(left.Code, right.Code, StringComparison.Ordinal))
        {
            return true;
        }
        if (!string.Equals(left.Code, right.Code, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        // Missing declarations retain the FHIR default; unknown versions cannot choose between conflicting declarations.
        bool? caseSensitive = version is null
            ? _caseSensitivityBySystem.GetValueOrDefault(left.SystemId, true)
            : _caseSensitivityByVersion.GetValueOrDefault((left.SystemId, version), true);
        if (caseSensitive is null)
        {
            TrackUnknownSystemVersion($"system {left.SystemId} (case sensitivity depends on the version)");
        }
        return caseSensitive == false;
    }

    private List<ValueSetExpansionRow> DistinctRows(IEnumerable<ValueSetExpansionRow> rows)
    {
        var seen = new Dictionary<(int SystemId, string Code), List<ValueSetExpansionRow>>(CandidateCodeComparer);
        return rows.Where(row => AddDistinct(seen, row)).ToList();
    }

    private bool AddDistinct(
        Dictionary<(int SystemId, string Code), List<ValueSetExpansionRow>> index, ValueSetExpansionRow row)
    {
        var key = (row.SystemId, row.Code);
        if (!index.TryGetValue(key, out var candidates))
        {
            index.Add(key, [row]);
            return true;
        }
        if (candidates.Any(existing => string.Equals(existing.SystemVersion, row.SystemVersion, StringComparison.Ordinal)
            && CodesMatch(existing, row, row.SystemVersion)))
        {
            return false;
        }
        candidates.Add(row);
        return true;
    }

    private void TrackUnknownSystemVersion(string description)
    {
        if (!_unknownSystemVersions.Contains(description))
        {
            _unknownSystemVersions.Add(description);
            _logger.LogWarning("Expansion cannot determine the system version required by '{System}'", description);
        }
    }

    private async Task ReadCodeComparisonPoliciesAsync(CancellationToken cancellationToken)
    {
#pragma warning disable CA2100
        using var command = new SqlCommand(
            $"SELECT {CodeSystems.Column("SystemId").Name}, {CodeSystems.Column("Version").Name}, " +
            $"{CodeSystems.Column("CaseSensitive").Name} FROM {Qualified(CodeSystems)} " +
            $"ORDER BY {CodeSystems.Column("ImportedDate").Name} DESC, {CodeSystems.Column("TermCodeSystemId").Name} DESC")
        {
            CommandTimeout = _commandTimeoutSeconds,
        };
#pragma warning restore CA2100
        var policies = await _sqlExecutionService.ExecuteReaderAsync(
            _systemPartitionId, command,
            reader => (SystemId: reader.GetInt32(0), Version: reader.IsDBNull(1) ? null : reader.GetString(1),
                CaseSensitive: reader.GetBoolean(2)), cancellationToken);
        foreach (var policy in policies)
        {
            _caseSensitivityByVersion.TryAdd((policy.SystemId, policy.Version), policy.CaseSensitive);
        }
        foreach (var system in _caseSensitivityByVersion.GroupBy(policy => policy.Key.SystemId))
        {
            _caseSensitivityBySystem.Add(system.Key,
                system.All(policy => policy.Value) ? true : system.All(policy => !policy.Value) ? false : null);
        }
    }

    private async Task<IReadOnlyList<ValueSetExpansionRow>> ReadReferencedValueSetAsync(
        string canonical, CancellationToken cancellationToken)
    {
        var valueSet = await ResolveExpandedValueSetAsync(canonical, cancellationToken);
        if (valueSet is null)
        {
            _logger.LogWarning("Compose references ValueSet '{Canonical}' that is not expanded", canonical);
            if (!_missingValueSets.Contains(canonical))
            {
                _missingValueSets.Add(canonical);
            }
            return [];
        }
        if (valueSet.Value.IsPartial)
        {
            _logger.LogWarning("Compose references partially expanded ValueSet '{Canonical}': {Reason}", canonical, valueSet.Value.Reason);
            _partialValueSets.TryAdd(canonical, valueSet.Value.Reason);
        }
        return await ReadExpansionAsync(valueSet.Value.Id, cancellationToken);
    }

    private async Task<List<ValueSetExpansionRow>> ReadFilteredSystemCodesAsync(
        int systemId, string system, string? version, JsonArray filters, bool isExclude, CancellationToken cancellationToken)
    {
        var candidates = await ReadConceptsAsync(systemId, version, cancellationToken);
        if (candidates.Count == 0)
        {
            // An include over a system with no local concepts is always partial. An exclude is partial only
            // when it could have mattered — codes from that system did get in, by way of a referenced
            // ValueSet — because then the filter that was meant to remove them could not be evaluated.
            if (!isExclude || _included.Any(row => row.SystemId == systemId))
            {
                TrackExternalSystem(system);
            }

            return [];
        }

        return ApplyFilters(candidates, filters, system).Select(concept => new ValueSetExpansionRow(
            systemId, concept.Code, concept.Display, concept.Version)).ToList();
    }

    private void TrackExternalSystem(string system)
    {
        if (_externalSystems.Contains(system))
        {
            return;
        }

        _logger.LogDebug("CodeSystem '{System}' has no imported concepts - marking expansion as partial", system);
        _externalSystems.Add(system);
    }

    /// <summary>
    /// Compiles every filter in the clause before evaluating any concept, so an operator this type cannot
    /// evaluate is caught once rather than per concept — and so it can select nothing instead of
    /// accidentally selecting everything.
    /// </summary>
    private List<ConceptCandidate> ApplyFilters(
        IReadOnlyList<ConceptCandidate> candidates, JsonArray filters, string system)
    {
        var byId = candidates.ToDictionary(c => c.Id);
        var predicates = new List<Func<ConceptCandidate, bool>>();

        foreach (var filter in ObjectsOf(filters))
        {
            var predicate = BuildPredicate(filter, byId);

            if (predicate is null)
            {
                TrackUnsupportedFilter(system, filter);
                return [];
            }

            predicates.Add(predicate);
        }

        return candidates.Where(candidate => predicates.All(predicate => predicate(candidate))).ToList();
    }

    private void TrackUnsupportedFilter(string system, JsonObject filter)
    {
        var description = $"{system}: {filter["property"]?.GetValue<string>() ?? "?"} " +
            $"{filter["op"]?.GetValue<string>() ?? "?"}";

        _logger.LogWarning("Compose filter '{Filter}' cannot be evaluated - marking expansion as partial", description);

        if (!_unsupportedFilters.Contains(description))
        {
            _unsupportedFilters.Add(description);
        }
    }

    private static Func<ConceptCandidate, bool>? BuildPredicate(
        JsonObject filter, Dictionary<long, ConceptCandidate> byId)
    {
        var property = filter["property"]?.GetValue<string>();
        var op = filter["op"]?.GetValue<string>();
        var value = filter["value"]?.GetValue<string>();

        if (string.IsNullOrEmpty(property) || string.IsNullOrEmpty(op) || string.IsNullOrEmpty(value))
        {
            return null;
        }

        return property switch
        {
            "code" => CodePredicate(op, value, byId),
            "display" => DisplayPredicate(op, value),
            _ => PropertyPredicate(property, op, value),
        };
    }

    private static Func<ConceptCandidate, bool>? CodePredicate(
        string op, string value, Dictionary<long, ConceptCandidate> byId) => op switch
        {
            "=" => concept => string.Equals(concept.Code, value, StringComparison.Ordinal),
            "in" => InPredicate(value, concept => concept.Code),
            "regex" => RegexPredicate(value, concept => concept.Code),

            // is-a includes the named code; descendent-of is the same walk without it. Conflating them
            // silently widened every descendent-of filter by one concept.
            "is-a" => concept => IsWithin(concept, value, byId, includeSelf: true),
            "descendent-of" => concept => IsWithin(concept, value, byId, includeSelf: false),
            _ => null,
        };

    private static Func<ConceptCandidate, bool>? DisplayPredicate(string op, string value) => op switch
    {
        "=" => concept => string.Equals(concept.Display, value, StringComparison.Ordinal),
        "contains" => concept => concept.Display?.Contains(value, StringComparison.OrdinalIgnoreCase) == true,
        "regex" => RegexPredicate(value, concept => concept.Display),
        _ => null,
    };

    private static Func<ConceptCandidate, bool>? PropertyPredicate(string property, string op, string value)
    {
        var match = op switch
        {
            "=" => new Func<string?, bool>(actual => string.Equals(actual, value, StringComparison.Ordinal)),
            "in" => actual => actual is not null && SplitValues(value).Contains(actual, StringComparer.Ordinal),
            "regex" => CompileRegex(value) is { } regex ? actual => actual is not null && regex.IsMatch(actual) : null,
            _ => null,
        };

        return match is null
            ? null
            : concept => ReadPropertyValues(concept.PropertiesJson, property).Any(match);
    }

    private static Func<ConceptCandidate, bool> InPredicate(string value, Func<ConceptCandidate, string?> selector)
    {
        var values = SplitValues(value);
        return concept => selector(concept) is { } actual && values.Contains(actual, StringComparer.Ordinal);
    }

    private static Func<ConceptCandidate, bool>? RegexPredicate(string value, Func<ConceptCandidate, string?> selector)
    {
        var regex = CompileRegex(value);
        return regex is null ? null : concept => selector(concept) is { } actual && regex.IsMatch(actual);
    }

    /// <summary>
    /// Returns null for a pattern that does not compile, which routes it through the unsupported-filter path
    /// rather than throwing partway through evaluation and failing the whole import over one bad filter. The
    /// timeout bounds a pathological pattern in package content this server does not author.
    /// </summary>
    private static Regex? CompileRegex(string pattern)
    {
        try
        {
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string[] SplitValues(string value)
        => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsWithin(
        ConceptCandidate concept, string ancestorCode, Dictionary<long, ConceptCandidate> byId, bool includeSelf)
    {
        if (includeSelf && string.Equals(concept.Code, ancestorCode, StringComparison.Ordinal))
        {
            return true;
        }

        var visited = new HashSet<long> { concept.Id };
        var current = concept;

        while (current.ParentId is { } parentId && visited.Add(parentId) && byId.TryGetValue(parentId, out var parent))
        {
            if (string.Equals(parent.Code, ancestorCode, StringComparison.Ordinal))
            {
                return true;
            }

            current = parent;
        }

        return false;
    }

    private static IEnumerable<string?> ReadPropertyValues(string? propertiesJson, string property)
    {
        if (string.IsNullOrEmpty(propertiesJson))
        {
            yield break;
        }

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(propertiesJson);
        }
        catch (JsonException)
        {
            yield break;
        }

        foreach (var entry in ObjectsOf((parsed as JsonObject)?["property"]))
        {
            if (!string.Equals(entry["code"]?.GetValue<string>(), property, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return entry["valueString"]?.GetValue<string>()
                ?? entry["valueCode"]?.GetValue<string>()
                ?? entry["valueCoding"]?["code"]?.GetValue<string>()
                ?? entry["valueBoolean"]?.GetValue<bool>().ToString();
        }
    }

    private string BuildPartialReason()
    {
        var reasons = new List<string>();

        if (_externalSystems.Count > 0)
        {
            reasons.Add($"External systems not imported: {string.Join(", ", _externalSystems)}");
        }

        if (_missingValueSets.Count > 0)
        {
            reasons.Add($"Referenced ValueSets not expanded: {string.Join(", ", _missingValueSets)}");
        }

        if (_partialValueSets.Count > 0)
        {
            reasons.Add("Referenced ValueSets partially expanded: " + string.Join("; ",
                _partialValueSets.Select(entry => $"{entry.Key}: {entry.Value ?? "completeness unknown"}")));
        }

        if (_unknownSystemVersions.Count > 0)
        {
            reasons.Add($"Codes with unknown system versions: {string.Join(", ", _unknownSystemVersions)}");
        }

        if (_unsupportedFilters.Count > 0)
        {
            reasons.Add($"Filters not evaluated: {string.Join(", ", _unsupportedFilters)}");
        }

        var reason = string.Join("; ", reasons);

        return reason.Length > PartialReasonMaxLength
            ? string.Concat(reason.AsSpan(0, PartialReasonMaxLength - 3), "...")
            : reason;
    }

    // An include creates the system row -- the codes it brings in have to reference one. An exclude only
    // removes codes that are already present, so a system nobody included cannot matter, and creating a row
    // for it would leave the database claiming to know a system it has never seen.
    private async Task<int?> ResolveSystemIdAsync(string system, bool isExclude, CancellationToken cancellationToken)
        => isExclude
            ? await _systemRepository.GetSystemIdAsync(system, cancellationToken)
            : await _systemRepository.GetOrCreateAsync(system, cancellationToken);

    private async Task<IReadOnlyList<ConceptCandidate>> ReadConceptsAsync(
        int systemId, string? version, CancellationToken cancellationToken)
    {
        var versionFilter = version is null
            ? string.Empty
            : $" AND cs.{CodeSystems.Column("Version").Name} = @version";

#pragma warning disable CA2100
        using var command = new SqlCommand(
            $"SELECT tc.{Concepts.Column("TermConceptId").Name}, tc.{Concepts.Column("Code").Name}, " +
            $"tc.{Concepts.Column("Display").Name}, tc.{Concepts.Column("ParentConceptId").Name}, " +
            $"tc.{Concepts.Column("PropertiesJson").Name}, cs.{CodeSystems.Column("Version").Name} " +
            $"FROM {Qualified(Concepts)} tc " +
            $"JOIN {Qualified(CodeSystems)} cs ON cs.{CodeSystems.Column("TermCodeSystemId").Name} = tc.{Concepts.Column("TermCodeSystemId").Name} " +
            $"WHERE cs.{CodeSystems.Column("SystemId").Name} = @systemId{versionFilter}")
        {
            CommandTimeout = _commandTimeoutSeconds,
        };
#pragma warning restore CA2100

        command.Parameters.AddWithValue("@systemId", systemId);
        if (version is not null)
        {
            command.Parameters.AddWithValue("@version", version);
        }

        return await _sqlExecutionService.ExecuteReaderAsync(
            _systemPartitionId,
            command,
            reader => new ConceptCandidate(
                Id: reader.GetInt64(0),
                Code: reader.GetString(1),
                Display: reader.IsDBNull(2) ? null : reader.GetString(2),
                ParentId: reader.IsDBNull(3) ? null : reader.GetInt64(3),
                PropertiesJson: reader.IsDBNull(4) ? null : reader.GetString(4),
                Version: reader.IsDBNull(5) ? null : reader.GetString(5)),
            cancellationToken);
    }

    // Left on the ADO default rather than _commandTimeoutSeconds: TOP 1 over IX_TermValueSet_Canonical is a
    // single-row index seek regardless of table size, unlike ReadConceptsAsync and ReadExpansionAsync below.
    private async Task<(long Id, bool IsPartial, string? Reason)?> ResolveExpandedValueSetAsync(
        string canonical, CancellationToken cancellationToken)
    {
        var reference = TerminologyCanonicalReference.Parse(canonical);
        var versionFilter = reference.Version is null ? string.Empty : $" AND {ValueSets.Column("Version").Name} = @version";
#pragma warning disable CA2100
        using var command = new SqlCommand(
            $"SELECT TOP 1 {ValueSets.Column("TermValueSetId").Name}, {ValueSets.Column("IsPartialExpansion").Name}, " +
            $"{ValueSets.Column("PartialExpansionReason").Name} FROM {Qualified(ValueSets)} " +
            $"WHERE {ValueSets.Column("Canonical").Name} = @canonical AND {ValueSets.Column("IsExpanded").Name} = 1 " +
            $"{versionFilter} ORDER BY {ValueSets.Column("ImportedDate").Name} DESC, {ValueSets.Column("TermValueSetId").Name} DESC");
#pragma warning restore CA2100

        command.Parameters.AddWithValue("@canonical", reference.Url);
        if (reference.Version is not null)
        {
            command.Parameters.AddWithValue("@version", reference.Version);
        }

        var rows = await _sqlExecutionService.ExecuteReaderAsync(
            _systemPartitionId, command,
            reader => (Id: reader.GetInt64(0), IsPartial: reader.GetBoolean(1), Reason: reader.IsDBNull(2) ? null : reader.GetString(2)),
            cancellationToken);

        return rows.Count > 0 ? rows[0] : null;
    }

    // Also unbounded, for the same reason as ReadConceptsAsync: a compose.include.valueSet naming a
    // previously expanded SNOMED-scale ValueSet reads every one of its rows back in one query.
    private async Task<IReadOnlyList<ValueSetExpansionRow>> ReadExpansionAsync(
        long valueSetId, CancellationToken cancellationToken)
    {
#pragma warning disable CA2100
        using var command = new SqlCommand(
            $"SELECT e.{Expansions.Column("SystemId").Name}, e.{Expansions.Column("Code").Name}, " +
            $"e.{Expansions.Column("Display").Name}, e.{Expansions.Column("SystemVersion").Name} " +
            $"FROM {Qualified(Expansions)} e " +
            $"WHERE e.{Expansions.Column("TermValueSetId").Name} = @valueSetId " +
            $"ORDER BY e.{Expansions.Column("Ordinal").Name}")
        {
            CommandTimeout = _commandTimeoutSeconds,
        };
#pragma warning restore CA2100

        command.Parameters.AddWithValue("@valueSetId", valueSetId);

        return await _sqlExecutionService.ExecuteReaderAsync(
            _systemPartitionId,
            command,
            reader => new ValueSetExpansionRow(
                SystemId: reader.GetInt32(0),
                Code: reader.GetString(1),
                Display: reader.IsDBNull(2) ? null : reader.GetString(2),
                SystemVersion: reader.IsDBNull(3) ? null : reader.GetString(3)),
            cancellationToken);
    }

    private static IEnumerable<JsonObject> ObjectsOf(JsonNode? node)
        => node is JsonArray array ? array.OfType<JsonObject>() : [];

    private static IEnumerable<string> CanonicalsOf(JsonArray? array)
        => array is null
            ? []
            : array.Select(node => node?.GetValue<string>()).OfType<string>().Where(value => value.Length > 0);

    private static string Qualified(TableDescriptor table) => $"{table.SchemaName}.{table.TableName}";

    private sealed record ConceptCandidate(
        long Id,
        string Code,
        string? Display,
        long? ParentId,
        string? PropertiesJson,
        string? Version);
}
