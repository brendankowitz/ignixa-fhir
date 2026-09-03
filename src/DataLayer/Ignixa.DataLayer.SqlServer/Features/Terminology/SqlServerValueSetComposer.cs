using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ignixa.Domain.Abstractions;
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

    private readonly ISqlExecutionService _sqlExecutionService;
    private readonly int _systemPartitionId;
    private readonly ISystemRepository _systemRepository;
    private readonly ILogger _logger;
    private readonly int _commandTimeoutSeconds;

    private readonly List<ValueSetExpansionRow> _included = [];
    private readonly HashSet<(int SystemId, string Code)> _includedKeys = [];
    private readonly HashSet<(int SystemId, string Code)> _excludedKeys = [];
    private readonly HashSet<int> _excludedSystems = [];
    private readonly List<string> _externalSystems = [];
    private readonly List<string> _missingValueSets = [];
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
        _commandTimeoutSeconds = commandTimeoutSeconds;
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
        foreach (var include in ObjectsOf(compose["include"]))
        {
            await ProcessClauseAsync(include, isExclude: false, cancellationToken);
        }

        foreach (var exclude in ObjectsOf(compose["exclude"]))
        {
            await ProcessClauseAsync(exclude, isExclude: true, cancellationToken);
        }

        var entries = _included
            .Where(row => !_excludedSystems.Contains(row.SystemId)
                && !_excludedKeys.Contains((row.SystemId, row.Code)))
            .ToList();

        var isPartial = _externalSystems.Count > 0 || _missingValueSets.Count > 0 || _unsupportedFilters.Count > 0;

        return new ComposedExpansion(entries, isPartial, isPartial ? BuildPartialReason() : null);
    }

    /// <summary>
    /// One <c>include</c> or <c>exclude</c>. The three sources are additive and the order is the FHIR
    /// element order: explicit concepts, referenced ValueSets, then the system itself — filtered if the
    /// clause carries filters, whole if it names nothing else.
    /// </summary>
    private async Task ProcessClauseAsync(JsonObject clause, bool isExclude, CancellationToken cancellationToken)
    {
        var system = clause["system"]?.GetValue<string>();
        var version = clause["version"]?.GetValue<string>();
        var concepts = clause["concept"] as JsonArray;
        var valueSets = clause["valueSet"] as JsonArray;
        var filters = clause["filter"] as JsonArray;

        if (concepts is { Count: > 0 })
        {
            await AddExplicitConceptsAsync(concepts, system, version, isExclude, cancellationToken);
        }

        foreach (var canonical in CanonicalsOf(valueSets))
        {
            await AddFromValueSetAsync(canonical, isExclude, cancellationToken);
        }

        if (system is null)
        {
            return;
        }

        if (filters is { Count: > 0 })
        {
            await AddFilteredSystemCodesAsync(system, version, filters, isExclude, cancellationToken);
        }
        else if (concepts is null or { Count: 0 } && valueSets is null or { Count: 0 })
        {
            await AddWholeSystemAsync(system, version, isExclude, cancellationToken);
        }
    }

    private async Task AddExplicitConceptsAsync(
        JsonArray concepts, string? clauseSystem, string? clauseVersion, bool isExclude, CancellationToken cancellationToken)
    {
        foreach (var concept in ObjectsOf(concepts))
        {
            var code = concept["code"]?.GetValue<string>();
            var system = concept["system"]?.GetValue<string>() ?? clauseSystem;

            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(system))
            {
                continue;
            }

            var systemId = await ResolveSystemIdAsync(system, isExclude, cancellationToken);
            if (systemId is null)
            {
                continue;
            }

            Add(systemId.Value, code, concept["display"]?.GetValue<string>(), clauseVersion, isExclude);
        }
    }

    private async Task AddFromValueSetAsync(string canonical, bool isExclude, CancellationToken cancellationToken)
    {
        var valueSetId = await ResolveExpandedValueSetIdAsync(canonical, cancellationToken);

        if (valueSetId is null)
        {
            // Reported for excludes as well as includes. An unresolvable include leaves codes out; an
            // unresolvable exclude leaves codes in that were meant to be removed, which is the worse of the
            // two and was the one the previous implementation passed over in silence.
            _logger.LogWarning("Compose references ValueSet '{Canonical}' that is not expanded", canonical);

            if (!_missingValueSets.Contains(canonical))
            {
                _missingValueSets.Add(canonical);
            }

            return;
        }

        foreach (var row in await ReadExpansionAsync(valueSetId.Value, cancellationToken))
        {
            Add(row.SystemId, row.Code, row.Display, row.SystemVersion, isExclude);
        }
    }

    private async Task AddWholeSystemAsync(
        string system, string? version, bool isExclude, CancellationToken cancellationToken)
    {
        var systemId = await ResolveSystemIdAsync(system, isExclude, cancellationToken);
        if (systemId is null)
        {
            return;
        }

        if (isExclude)
        {
            // Held as a system rather than expanded into its codes: the exclusion has to cover codes that
            // arrived from an included ValueSet too, not just the ones this database happens to know.
            _excludedSystems.Add(systemId.Value);
            return;
        }

        var concepts = await ReadConceptsAsync(systemId.Value, version, cancellationToken);
        if (concepts.Count == 0)
        {
            TrackExternalSystem(system);
            return;
        }

        foreach (var concept in concepts)
        {
            Add(systemId.Value, concept.Code, concept.Display, concept.Version, isExclude: false);
        }
    }

    private async Task AddFilteredSystemCodesAsync(
        string system, string? version, JsonArray filters, bool isExclude, CancellationToken cancellationToken)
    {
        var systemId = await ResolveSystemIdAsync(system, isExclude, cancellationToken);
        if (systemId is null)
        {
            return;
        }

        var candidates = await ReadConceptsAsync(systemId.Value, version, cancellationToken);
        if (candidates.Count == 0)
        {
            // An include over a system with no local concepts is always partial. An exclude is partial only
            // when it could have mattered — codes from that system did get in, by way of a referenced
            // ValueSet — because then the filter that was meant to remove them could not be evaluated.
            if (!isExclude || _included.Any(row => row.SystemId == systemId.Value))
            {
                TrackExternalSystem(system);
            }

            return;
        }

        foreach (var concept in ApplyFilters(candidates, filters, system))
        {
            Add(systemId.Value, concept.Code, concept.Display, concept.Version, isExclude);
        }
    }

    private void Add(int systemId, string code, string? display, string? systemVersion, bool isExclude)
    {
        if (isExclude)
        {
            _excludedKeys.Add((systemId, code));
            return;
        }

        if (_includedKeys.Add((systemId, code)))
        {
            _included.Add(new ValueSetExpansionRow(systemId, code, display, systemVersion));
        }
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
    private async Task<long?> ResolveExpandedValueSetIdAsync(string canonical, CancellationToken cancellationToken)
    {
#pragma warning disable CA2100
        using var command = new SqlCommand(
            $"SELECT TOP 1 {ValueSets.Column("TermValueSetId").Name} FROM {Qualified(ValueSets)} " +
            $"WHERE {ValueSets.Column("Canonical").Name} = @canonical AND {ValueSets.Column("IsExpanded").Name} = 1 " +
            $"ORDER BY {ValueSets.Column("ImportedDate").Name} DESC");
#pragma warning restore CA2100

        command.Parameters.AddWithValue("@canonical", canonical);

        var rows = await _sqlExecutionService.ExecuteReaderAsync(
            _systemPartitionId, command, reader => reader.GetInt64(0), cancellationToken);

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
