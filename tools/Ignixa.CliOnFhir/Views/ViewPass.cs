// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;

namespace Ignixa.CliOnFhir.Views;

/// <summary>
/// One pass over the rows of a view that does everything the run needs of them: counts them, keeps the ones the response
/// returns, writes them to a file, and counts the distinct values of a column.
/// </summary>
internal sealed class ViewPass
{
    private readonly HashSet<string> _distinct = new(StringComparer.Ordinal);
    private readonly long[] _nonNull;

    private ViewPass(List<ViewRow> kept, int columns)
    {
        Kept = kept;
        _nonNull = new long[columns];
    }

    /// <summary>Every row the view produced.</summary>
    public long Total { get; private set; }

    /// <summary>The rows from the offset, at most the count: the page, or, when writing a file, its first rows (for evidence).</summary>
    public List<ViewRow> Kept { get; }

    /// <summary>Distinct non-null values of the distinct column.</summary>
    public long DistinctValues => _distinct.Count;

    /// <summary>Rows whose distinct column is null.</summary>
    public long DistinctNulls { get; private set; }

    /// <summary>The indexes of the columns that are null in every row; none when the view produced no rows.</summary>
    public IReadOnlyList<int> EmptyColumns => Total == 0 ? [] : [.. Enumerable.Range(0, _nonNull.Length).Where(index => _nonNull[index] == 0)];

    /// <param name="rows">The view's rows, in order.</param>
    /// <param name="columns">How many columns a row has.</param>
    /// <param name="keep">Which rows to keep: from this offset, this many.</param>
    /// <param name="distinctColumn">The index of the column to count distinct values of; null for none.</param>
    /// <param name="sink">The file to write every row to; null to write none.</param>
    public static async Task<ViewPass> RunAsync(IEnumerable<ViewRow> rows, int columns, ViewPaging keep, int? distinctColumn, ViewFileSink? sink, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var pass = new ViewPass([], columns);
        foreach (ViewRow row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sink is not null)
            {
                await sink.WriteAsync(row.Cells, cancellationToken);
            }

            pass.Count(row, distinctColumn);
            if (pass.Total >= keep.Offset && pass.Kept.Count < keep.Count)
            {
                pass.Kept.Add(row);
            }

            pass.Total++;
        }

        return pass;
    }

    private void Count(ViewRow row, int? distinctColumn)
    {
        for (int i = 0; i < _nonNull.Length; i++)
        {
            if (row.Cells[i] is not null)
            {
                _nonNull[i]++;
            }
        }

        CountDistinct(row, distinctColumn);
    }

    private void CountDistinct(ViewRow row, int? column)
    {
        if (column is not int index)
        {
            return;
        }

        if (row.Cells[index] is { } value)
        {
            _distinct.Add(Convert.ToString(value, CultureInfo.InvariantCulture)!);
        }
        else
        {
            DistinctNulls++;
        }
    }
}
