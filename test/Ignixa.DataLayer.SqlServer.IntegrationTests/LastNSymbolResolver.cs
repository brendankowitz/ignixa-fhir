using Ignixa.Search.Models;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

internal sealed class LastNSymbolResolver : ISymbolResolver
{
    private const short ObservationResourceTypeId = 104;
    private const short CodeSearchParamId = 210;
    private const short EffectiveSearchParamId = 211;
    private const short AuthorizationSearchParamId = 213;
    private const string CodeSearchParameterUrl = "http://hl7.org/fhir/SearchParameter/Observation-code";
    private const string EffectiveSearchParameterUrl = "http://hl7.org/fhir/SearchParameter/Observation-date";
    private const string AuthorizationSearchParameterUrl = "http://example.org/fhir/SearchParameter/Observation-authorization";

    public Task<short?> GetSearchParamIdAsync(
        SearchParameterInfo parameter,
        CancellationToken cancellationToken)
        => Task.FromResult<short?>(parameter.Url?.ToString() switch
        {
            CodeSearchParameterUrl => CodeSearchParamId,
            EffectiveSearchParameterUrl => EffectiveSearchParamId,
            AuthorizationSearchParameterUrl => AuthorizationSearchParamId,
            _ => null,
        });

    public Task<short?> GetResourceTypeIdAsync(
        string resourceType,
        CancellationToken cancellationToken)
        => Task.FromResult<short?>(
            string.Equals(resourceType, "Observation", StringComparison.Ordinal)
                ? ObservationResourceTypeId
                : null);

    public Task<int?> GetSystemIdAsync(string system, CancellationToken cancellationToken)
        => Task.FromResult<int?>(null);

    public Task<int?> GetQuantityCodeIdAsync(string code, CancellationToken cancellationToken)
        => Task.FromResult<int?>(null);
}
