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
        Add(command, "@ReferenceSearchParams", "dbo.ReferenceSearchParamList", Reference);
        Add(command, "@TokenSearchParams", "dbo.TokenSearchParamList", Token);
        Add(command, "@TokenTexts", "dbo.TokenTextList", TokenText);
        Add(command, "@StringSearchParams", "dbo.StringSearchParamList", String);
        Add(command, "@UriSearchParams", "dbo.UriSearchParamList", Uri);
        Add(command, "@NumberSearchParams", "dbo.NumberSearchParamList", Number);
        Add(command, "@QuantitySearchParams", "dbo.QuantitySearchParamList", Quantity);
        Add(command, "@DateTimeSearchParams", "dbo.DateTimeSearchParamList", DateTime);
        Add(command, "@ReferenceTokenCompositeSearchParams", "dbo.ReferenceTokenCompositeSearchParamList", ReferenceTokenComposite);
        Add(command, "@TokenTokenCompositeSearchParams", "dbo.TokenTokenCompositeSearchParamList", TokenTokenComposite);
        Add(command, "@TokenDateTimeCompositeSearchParams", "dbo.TokenDateTimeCompositeSearchParamList", TokenDateTimeComposite);
        Add(command, "@TokenQuantityCompositeSearchParams", "dbo.TokenQuantityCompositeSearchParamList", TokenQuantityComposite);
        Add(command, "@TokenStringCompositeSearchParams", "dbo.TokenStringCompositeSearchParamList", TokenStringComposite);
        Add(command, "@TokenNumberNumberCompositeSearchParams", "dbo.TokenNumberNumberCompositeSearchParamList", TokenNumberNumberComposite);
    }

    private static void Add(SqlCommand command, string name, string typeName, IList<SqlDataRecord>? records) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.Structured) { TypeName = typeName, Value = records });
}
