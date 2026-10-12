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
    IList<SqlDataRecord>? TokenNumberNumberComposite);
