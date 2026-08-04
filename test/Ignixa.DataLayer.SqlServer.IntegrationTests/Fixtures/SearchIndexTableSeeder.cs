using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Shouldly;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;

/// <summary>
/// Produces a <c>SearchIndices</c> collection that lands at least one row in every one of the 15
/// tables <c>SqlServerFhirRepository.SearchIndexTables</c> wipes, plus the assertion helpers that
/// prove those rows were really there before a delete and really gone after it.
/// <para>
/// Both delete paths (<c>DeleteAsync</c>'s per-surrogate wipe and <c>HardDeleteResourceAsync</c>'s
/// whole-resource wipe) issue a DELETE against the same fixed 15-table list. A sweep test that only
/// asserts "all 15 tables are empty afterwards" passes trivially when the tables were empty to begin
/// with -- it would pass just as happily against a repository that deletes none of them. Hence
/// <see cref="AssertEverySearchIndexTableHasRowsAsync"/>: the pre-condition is asserted per table, by
/// name, so a value shape that silently stops being indexed fails loudly instead of quietly
/// weakening the sweep.
/// </para>
/// <para>
/// <c>dbo.ResourceWriteClaim</c> is the one table the write path cannot populate --
/// <c>ResourceWriteClaimRowGenerator</c> is a documented Phase 1 stub that yields no rows -- so
/// <see cref="InsertResourceWriteClaimAsync"/> inserts its row directly. Without that the 15th table
/// would be the one untested member of the list the delete SQL is generated from.
/// </para>
/// </summary>
internal static class SearchIndexTableSeeder
{
    /// <summary>Mirrors <c>SqlServerFhirRepository.SearchIndexTables</c> exactly.</summary>
    public static readonly string[] SearchIndexTables =
    [
        "ReferenceSearchParam",
        "TokenSearchParam",
        "TokenText",
        "StringSearchParam",
        "UriSearchParam",
        "NumberSearchParam",
        "QuantitySearchParam",
        "DateTimeSearchParam",
        "ReferenceTokenCompositeSearchParam",
        "TokenTokenCompositeSearchParam",
        "TokenDateTimeCompositeSearchParam",
        "TokenQuantityCompositeSearchParam",
        "TokenStringCompositeSearchParam",
        "TokenNumberNumberCompositeSearchParam",
        "ResourceWriteClaim",
    ];

    private const string UrlPrefix = "http://example.org/fhir/SearchParameter/sweep-";

    private static readonly string[] ParameterUrls =
    [
        UrlPrefix + "token",
        UrlPrefix + "string",
        UrlPrefix + "uri",
        UrlPrefix + "number",
        UrlPrefix + "quantity",
        UrlPrefix + "datetime",
        UrlPrefix + "reference",
        UrlPrefix + "token-token",
        UrlPrefix + "token-datetime",
        UrlPrefix + "token-quantity",
        UrlPrefix + "token-string",
        UrlPrefix + "token-number-number",
        UrlPrefix + "reference-token",
    ];

    /// <summary>
    /// Inserts a <c>dbo.SearchParam</c> catalog row for every parameter
    /// <see cref="BuildSearchIndicesCoveringEverySearchIndexTable"/> uses. Must run before the first
    /// write: the merge path loads the catalog once (<c>EnsureSearchParametersPreloadedAsync</c>) and
    /// every row generator silently skips a parameter whose URL has no id.
    /// </summary>
    public static async Task SeedSearchParameterCatalogAsync(TestTenantDatabase database, CancellationToken cancellationToken)
    {
        foreach (var url in ParameterUrls)
        {
            await database.ExecuteNonQueryAsync(
                "INSERT INTO dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported) " +
                $"VALUES ('{url}', 'active', SYSDATETIMEOFFSET(), 0)",
                cancellationToken);
        }
    }

    /// <summary>
    /// One search index entry per row generator, shaped so no two entries collide in a generator that
    /// dispatches on value shape (e.g. the token/string composite's second component holds no
    /// <c>TokenSearchValue</c>, so the token/token composite generator skips it).
    /// </summary>
    public static IReadOnlyList<object> BuildSearchIndicesCoveringEverySearchIndexTable(string referenceTargetId)
    {
        // A code-only token: a non-null system needs a pre-seeded dbo.System cache entry to resolve a
        // SystemId, and the generators skip the record outright when it does not resolve. Text is
        // non-empty so the same entry also produces the dbo.TokenText row.
        var token = new TokenSearchValue(system: null, code: "sweep-code", text: "sweep text");
        var quantity = new QuantitySearchValue(system: null, code: null, low: 1m, high: 2m);

        return
        [
            new SearchIndexEntry(Parameter(UrlPrefix + "token", SearchParamType.Token), token),
            new SearchIndexEntry(Parameter(UrlPrefix + "string", SearchParamType.String), new StringSearchValue("sweep-string")),
            new SearchIndexEntry(Parameter(UrlPrefix + "uri", SearchParamType.Uri), new UriSearchValue("http://example.org/sweep-uri", separateCanonicalComponents: false)),
            new SearchIndexEntry(Parameter(UrlPrefix + "number", SearchParamType.Number), new NumberSearchValue(low: 10m, high: 20m)),
            new SearchIndexEntry(Parameter(UrlPrefix + "quantity", SearchParamType.Quantity), quantity),
            new SearchIndexEntry(Parameter(UrlPrefix + "datetime", SearchParamType.Date), new DateTimeSearchValue(new DateTimeOffset(2024, 3, 4, 5, 6, 7, TimeSpan.Zero))),
            new SearchIndexEntry(
                Parameter(UrlPrefix + "reference", SearchParamType.Reference),
                new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: referenceTargetId)),
            new SearchIndexEntry(
                Parameter(UrlPrefix + "token-token", SearchParamType.Composite),
                new CompositeIndexSearchValue([[token], [new TokenSearchValue(system: null, code: "sweep-code-2", text: null)]])),
            new SearchIndexEntry(
                Parameter(UrlPrefix + "token-datetime", SearchParamType.Composite),
                new CompositeIndexSearchValue([[token], [new DateTimeSearchValue(new DateTimeOffset(2024, 3, 4, 5, 6, 7, TimeSpan.Zero))]])),
            new SearchIndexEntry(
                Parameter(UrlPrefix + "token-quantity", SearchParamType.Composite),
                new CompositeIndexSearchValue([[token], [quantity]])),
            new SearchIndexEntry(
                Parameter(UrlPrefix + "token-string", SearchParamType.Composite),
                new CompositeIndexSearchValue([[token], [new StringSearchValue("sweep-composite-string")]])),
            new SearchIndexEntry(
                Parameter(UrlPrefix + "token-number-number", SearchParamType.Composite),
                new CompositeIndexSearchValue([[token], [new NumberSearchValue(low: 1m, high: 2m)], [new NumberSearchValue(low: 3m, high: 4m)]])),
            new SearchIndexEntry(
                Parameter(UrlPrefix + "reference-token", SearchParamType.Composite),
                new CompositeIndexSearchValue(
                [
                    [new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: referenceTargetId)],
                    [new TokenSearchValue(system: null, code: "sweep-relation", text: null)],
                ])),
        ];
    }

    /// <summary>
    /// Inserts the one row the write path cannot produce (see this type's remarks), against the same
    /// surrogate id the resource's own index rows carry.
    /// </summary>
    public static Task InsertResourceWriteClaimAsync(TestTenantDatabase database, long resourceSurrogateId, CancellationToken cancellationToken) =>
        database.ExecuteNonQueryAsync(
            "INSERT INTO dbo.ResourceWriteClaim (ResourceSurrogateId, ClaimTypeId, ClaimValue) " +
            $"VALUES ({resourceSurrogateId}, 1, 'sweep-claim')",
            cancellationToken);

    public static async Task AssertEverySearchIndexTableHasRowsAsync(
        TestTenantDatabase database, long resourceSurrogateId, CancellationToken cancellationToken)
    {
        foreach (var table in SearchIndexTables)
        {
            var rowCount = await database.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM dbo.{table} WHERE ResourceSurrogateId = {resourceSurrogateId}", cancellationToken);
            rowCount.ShouldBeGreaterThan(
                0,
                $"dbo.{table} has no row for this resource before the delete, so asserting it is empty afterwards would prove nothing.");
        }
    }

    public static async Task AssertEverySearchIndexTableIsEmptyAsync(
        TestTenantDatabase database, long resourceSurrogateId, CancellationToken cancellationToken)
    {
        foreach (var table in SearchIndexTables)
        {
            var rowCount = await database.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM dbo.{table} WHERE ResourceSurrogateId = {resourceSurrogateId}", cancellationToken);
            rowCount.ShouldBe(0, $"dbo.{table} still has rows for this resource after the delete.");
        }
    }

    private static SearchParameterInfo Parameter(string url, SearchParamType type) =>
        new(url[(url.LastIndexOf('/') + 1)..], url[(url.LastIndexOf('/') + 1)..], type, new Uri(url));
}
