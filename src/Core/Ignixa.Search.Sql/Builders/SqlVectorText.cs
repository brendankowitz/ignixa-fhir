using System.Globalization;
using System.Text;

namespace Ignixa.Search.Sql.Builders;

/// <summary>
/// The one text form a <c>float</c> vector takes on its way into SQL Server: a JSON array that
/// <c>CAST(… AS vector(n))</c> accepts, with every element in invariant round-trip (<c>"R"</c>) format. Shared
/// by the query-time embedding parameter and the index writer so a stored vector and a query vector can never
/// be serialized differently.
/// </summary>
public static class SqlVectorText
{
    /// <summary>
    /// Formats <paramref name="values"/> as <c>[v0,v1,…]</c>. "R" is lossless for <see cref="float"/>, and every
    /// form it produces — exponents, subnormals such as <c>1E-45</c>, and <c>-0</c> — parses through
    /// <c>CAST(… AS vector(n))</c> on SQL Server 2025, so no alternate notation is needed.
    /// </summary>
    public static string Format(ReadOnlySpan<float> values)
    {
        var builder = new StringBuilder((values.Length * 12) + 2);
        builder.Append('[');
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(values[i].ToString("R", CultureInfo.InvariantCulture));
        }

        builder.Append(']');
        return builder.ToString();
    }
}
