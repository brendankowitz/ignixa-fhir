using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql;

/// <summary>
/// The search parameter codes whose values live on the resource row itself (<c>dbo.Resource</c>) rather
/// than in a search-parameter table.
/// </summary>
/// <remarks>
/// <para>
/// These carry no SearchParamId, so any caller that resolves, dispatches, or classifies by SearchParamId
/// must skip them. The distinction is also what makes a <c>_sort</c> key "custom": a sort on one of these
/// orders by a column the keyset boundary already identifies, whereas a sort on any other parameter needs
/// a join and a captured sort value.
/// </para>
/// <para>
/// This is public because a host cannot derive the set from anything else on the public surface. It is the
/// same classification <see cref="SortKeyKind"/> draws after lowering — <see cref="SortKeyKind.LastUpdated"/>,
/// <see cref="SortKeyKind.ResourceType"/> and <see cref="SortKeyKind.ResourceId"/> are exactly these three —
/// but a host must often make the call <em>before</em> compiling, while it still holds only parameter codes:
/// deciding whether a continuation token can be reconstructed into a typed or typeless page boundary, for
/// instance, has to happen before there is a plan to inspect. Without this, hosts duplicate the literal set
/// and drift from the compiler.
/// </para>
/// </remarks>
public static class ResourceColumnParameters
{
    /// <summary>
    /// True when <paramref name="parameterCode"/> names a search parameter backed by a
    /// <c>dbo.Resource</c> column rather than a search-parameter table.
    /// </summary>
    /// <param name="parameterCode">The search parameter code, for example <c>_lastUpdated</c>.</param>
    /// <returns>Whether the parameter is backed by a resource column.</returns>
    public static bool IsResourceColumnCode(string parameterCode)
        => parameterCode is "_id" or "_type" or "_lastUpdated";
}
