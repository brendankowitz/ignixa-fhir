using Microsoft.Data.SqlClient.Server;

namespace Ignixa.DataLayer.SqlServer;

public sealed record SqlResourceIndexBatch(
    IReadOnlyList<SqlDataRecord>? Resources = null,
    IReadOnlyList<SqlDataRecord>? ResourceWriteClaims = null,
    IReadOnlyList<SqlDataRecord>? ReferenceSearchParams = null,
    IReadOnlyList<SqlDataRecord>? TokenSearchParams = null,
    IReadOnlyList<SqlDataRecord>? TokenTexts = null,
    IReadOnlyList<SqlDataRecord>? StringSearchParams = null,
    IReadOnlyList<SqlDataRecord>? UriSearchParams = null,
    IReadOnlyList<SqlDataRecord>? NumberSearchParams = null,
    IReadOnlyList<SqlDataRecord>? QuantitySearchParams = null,
    IReadOnlyList<SqlDataRecord>? DateTimeSearchParams = null,
    IReadOnlyList<SqlDataRecord>? ReferenceTokenCompositeSearchParams = null,
    IReadOnlyList<SqlDataRecord>? TokenTokenCompositeSearchParams = null,
    IReadOnlyList<SqlDataRecord>? TokenDateTimeCompositeSearchParams = null,
    IReadOnlyList<SqlDataRecord>? TokenQuantityCompositeSearchParams = null,
    IReadOnlyList<SqlDataRecord>? TokenStringCompositeSearchParams = null,
    IReadOnlyList<SqlDataRecord>? TokenNumberNumberCompositeSearchParams = null);
