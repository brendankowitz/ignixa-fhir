using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.RowGenerators;
using Ignixa.Domain.Models;
using Microsoft.Data.SqlClient.Server;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// Builds the search-index table-valued parameter row sets shared by the merge and reindex write paths.
/// </summary>
internal sealed class SearchIndexRowSetBuilder(SqlServerSearchIndexReferenceDataCache referenceDataCache)
{
    private readonly ISearchParameterRowGenerator _referenceRowGenerator = new ReferenceSearchParameterRowGenerator();
    private readonly ISearchParameterRowGenerator _stringRowGenerator = new StringSearchParameterRowGenerator();
    private readonly ISearchParameterRowGenerator _numberRowGenerator = new NumberSearchParameterRowGenerator();
    private readonly ISearchParameterRowGenerator _quantityRowGenerator =
        new QuantitySearchParameterRowGenerator(referenceDataCache.SystemMappings, referenceDataCache.QuantityCodeMappings);
    private readonly ISearchParameterRowGenerator _dateTimeRowGenerator = new DateTimeSearchParameterRowGenerator();
    private readonly ISearchParameterRowGenerator _tokenTextRowGenerator = new TokenTextRowGenerator();
    private readonly ISearchParameterRowGenerator _refTokenCompositeRowGenerator =
        new RefTokenCompositeRowGenerator(referenceDataCache.SystemMappings);
    private readonly ISearchParameterRowGenerator _tokenTokenCompositeRowGenerator =
        new TokenTokenCompositeRowGenerator(referenceDataCache.SystemMappings);
    private readonly ISearchParameterRowGenerator _tokenDateTimeCompositeRowGenerator =
        new TokenDateTimeCompositeRowGenerator(referenceDataCache.SystemMappings);
    private readonly ISearchParameterRowGenerator _tokenQuantityCompositeRowGenerator =
        new TokenQuantityCompositeRowGenerator(referenceDataCache.SystemMappings, referenceDataCache.QuantityCodeMappings);
    private readonly ISearchParameterRowGenerator _tokenStringCompositeRowGenerator =
        new TokenStringCompositeRowGenerator(referenceDataCache.SystemMappings);
    private readonly ISearchParameterRowGenerator _tokenNumberNumberCompositeRowGenerator =
        new TokenNumberNumberCompositeRowGenerator(referenceDataCache.SystemMappings);

    /// <summary>Token generator; also the source of token extension-column data.</summary>
    public TokenSearchParameterRowGenerator TokenRowGenerator { get; } = new(referenceDataCache.SystemMappings);

    /// <summary>URI generator; also the source of URI extension-column data.</summary>
    public UriSearchParameterRowGenerator UriRowGenerator { get; } = new();

    public SearchIndexRowSets Build(
        IReadOnlyList<ResourceWrapper> resources,
        IReadOnlyDictionary<string, short> resourceTypeIdMap,
        IReadOnlyDictionary<string, short> searchParameterIdMap,
        IReadOnlyDictionary<ResourceWrapper, long> resourceSurrogateIdMap,
        ILogger logger)
    {
        IList<SqlDataRecord>? Generate(ISearchParameterRowGenerator generator)
        {
            var records = generator.GenerateSqlDataRecords(
                resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, logger);
            var list = records as IList<SqlDataRecord> ?? records.ToList();
            return list.Count == 0 ? null : list;
        }

        // Generation order matches the stored-procedure parameter order.
        return new SearchIndexRowSets(
            Reference: Generate(_referenceRowGenerator),
            Token: Generate(TokenRowGenerator),
            TokenText: Generate(_tokenTextRowGenerator),
            String: Generate(_stringRowGenerator),
            Uri: Generate(UriRowGenerator),
            Number: Generate(_numberRowGenerator),
            Quantity: Generate(_quantityRowGenerator),
            DateTime: Generate(_dateTimeRowGenerator),
            ReferenceTokenComposite: Generate(_refTokenCompositeRowGenerator),
            TokenTokenComposite: Generate(_tokenTokenCompositeRowGenerator),
            TokenDateTimeComposite: Generate(_tokenDateTimeCompositeRowGenerator),
            TokenQuantityComposite: Generate(_tokenQuantityCompositeRowGenerator),
            TokenStringComposite: Generate(_tokenStringCompositeRowGenerator),
            TokenNumberNumberComposite: Generate(_tokenNumberNumberCompositeRowGenerator));
    }
}
