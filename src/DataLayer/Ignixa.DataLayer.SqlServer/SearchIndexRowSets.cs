using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;

namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// The search-index table-valued parameter row sets for a batch of resources. Each set is
/// <c>null</c> when empty, because SQL Client requires NULL (not an empty list) for a TVP with no rows.
/// </summary>
internal sealed record SearchIndexRowSets(
    IList<SqlDataRecord>? Reference,
    IList<SqlDataRecord>? Token,
    IList<SqlDataRecord>? TokenText,
    IList<SqlDataRecord>? String,
    IList<SqlDataRecord>? Uri,
    IList<SqlDataRecord>? Number,
    IList<SqlDataRecord>? Quantity,
    IList<SqlDataRecord>? DateTime,
    IList<SqlDataRecord>? ReferenceTokenComposite,
    IList<SqlDataRecord>? TokenTokenComposite,
    IList<SqlDataRecord>? TokenDateTimeComposite,
    IList<SqlDataRecord>? TokenQuantityComposite,
    IList<SqlDataRecord>? TokenStringComposite,
    IList<SqlDataRecord>? TokenNumberNumberComposite)
{
    /// <summary>
    /// Adds the sets as the search-index TVP parameters that MergeResources and UpdateResourceSearchParams
    /// both take, named as the command text binds them.
    /// </summary>
    public void AddTableValuedParameters(SqlCommand command)
    {
        AddTableValuedParameter(command, "@ReferenceSearchParams", "dbo.ReferenceSearchParamList", Reference);
        AddTableValuedParameter(command, "@TokenSearchParams", "dbo.TokenSearchParamList", Token);
        AddTableValuedParameter(command, "@TokenTexts", "dbo.TokenTextList", TokenText);
        AddTableValuedParameter(command, "@StringSearchParams", "dbo.StringSearchParamList", String);
        AddTableValuedParameter(command, "@UriSearchParams", "dbo.UriSearchParamList", Uri);
        AddTableValuedParameter(command, "@NumberSearchParams", "dbo.NumberSearchParamList", Number);
        AddTableValuedParameter(command, "@QuantitySearchParams", "dbo.QuantitySearchParamList", Quantity);
        AddTableValuedParameter(command, "@DateTimeSearchParams", "dbo.DateTimeSearchParamList", DateTime);
        AddTableValuedParameter(command, "@ReferenceTokenCompositeSearchParams", "dbo.ReferenceTokenCompositeSearchParamList", ReferenceTokenComposite);
        AddTableValuedParameter(command, "@TokenTokenCompositeSearchParams", "dbo.TokenTokenCompositeSearchParamList", TokenTokenComposite);
        AddTableValuedParameter(command, "@TokenDateTimeCompositeSearchParams", "dbo.TokenDateTimeCompositeSearchParamList", TokenDateTimeComposite);
        AddTableValuedParameter(command, "@TokenQuantityCompositeSearchParams", "dbo.TokenQuantityCompositeSearchParamList", TokenQuantityComposite);
        AddTableValuedParameter(command, "@TokenStringCompositeSearchParams", "dbo.TokenStringCompositeSearchParamList", TokenStringComposite);
        AddTableValuedParameter(command, "@TokenNumberNumberCompositeSearchParams", "dbo.TokenNumberNumberCompositeSearchParamList", TokenNumberNumberComposite);
    }

    internal static void AddTableValuedParameter(SqlCommand command, string name, string typeName, IList<SqlDataRecord>? records) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.Structured) { TypeName = typeName, Value = records });
}
