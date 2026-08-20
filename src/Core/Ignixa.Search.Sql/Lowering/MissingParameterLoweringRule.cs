using Ignixa.Search.Models;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>Resolves the index table a <c>:missing</c> presence set reads. A <c>:missing</c> search asks whether
/// the parameter has any index row at all, so it needs the table by parameter type rather than a value
/// predicate — the one mapping shared by simple and composite parameters.</summary>
internal static class MissingParameterLoweringRule
{
    public static TableDescriptor ResolveMissingTable(SearchParameterInfo parameter)
    {
        if (parameter.Type == SearchParamType.Composite)
        {
            return ResolveMissingCompositeTable(parameter);
        }

        var tableName = parameter.Type switch
        {
            SearchParamType.String => "StringSearchParam",
            SearchParamType.Token => "TokenSearchParam",
            SearchParamType.Reference => "ReferenceSearchParam",
            SearchParamType.Uri => "UriSearchParam",
            SearchParamType.Number => "NumberSearchParam",
            SearchParamType.Quantity => "QuantitySearchParam",
            SearchParamType.Date => "DateTimeSearchParam",
            _ => throw new NotSupportedException(
                $":missing is not supported for search parameter type '{parameter.Type}' on '{parameter.Code}'."),
        };

        return SqlCatalog.Default.Table(tableName);
    }

    private static TableDescriptor ResolveMissingCompositeTable(SearchParameterInfo parameter)
    {
        var componentTypes = parameter.Component.Select(c => c.ResolvedSearchParameter?.Type).ToArray();

        var tableName = componentTypes switch
        {
            [SearchParamType.Token, SearchParamType.Token] => "TokenTokenCompositeSearchParam",
            [SearchParamType.Token, SearchParamType.Number, SearchParamType.Number] => "TokenNumberNumberCompositeSearchParam",
            [SearchParamType.Token, SearchParamType.String] => "TokenStringCompositeSearchParam",
            [SearchParamType.Token, SearchParamType.Quantity] => "TokenQuantityCompositeSearchParam",
            [SearchParamType.Token, SearchParamType.Date] => "TokenDateTimeCompositeSearchParam",
            [SearchParamType.Reference, SearchParamType.Token] => "ReferenceTokenCompositeSearchParam",
            [SearchParamType.Token, SearchParamType.Reference] => "ReferenceTokenCompositeSearchParam",
            var types => throw new NotSupportedException(
                $":missing is not supported for composite search parameter '{parameter.Code}' with component types " +
                $"[{string.Join(", ", types.Select(t => t?.ToString() ?? "unresolved"))}] -- no matching composite table."),
        };

        return SqlCatalog.Default.Table(tableName);
    }
}
