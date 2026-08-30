using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Ignixa.Search.Sql.Generators;

/// <summary>
/// Parses CREATE TABLE dbo.X (...) blocks out of a raw T-SQL DDL script -- no Roslyn dependency,
/// directly unit-testable. Depth-aware: DECIMAL(36,18)'s internal comma must not be mistaken for a
/// column separator, and the table body's closing paren must not be mistaken for the type-args'
/// closing paren.
/// </summary>
public static class DdlTableParser
{
    private static readonly Regex TableStart = new(@"CREATE TABLE dbo\.(\w+)\s*\(", RegexOptions.IgnoreCase);

    private static readonly Regex ColumnLine = new(
        @"^(?<name>\w+)\s+(?<type>\w+)\s*(\((?<args>[^)]*)\))?" +
        @"(\s+COLLATE\s+(?<collation>\S+))?" +
        @"(\s+CONSTRAINT\s+\S+\s+DEFAULT\s+\S+)?" +
        @"(\s+DEFAULT\s+\S+)?" +
        @"(\s+IDENTITY\s*\([^)]*\))?" +
        // Mandatory, not optional: ParseColumns only ever runs on catalog-filtered tables (the
        // tableNameFilter check gates it before this regex sees a line), and those all declare
        // nullability explicitly -- the 65 columns elsewhere that rely on SQL Server's implicit-nullable
        // default live in non-catalog tables this parser never reaches. Making the group optional let a
        // NOT NULL following any CONSTRAINT clause other than "CONSTRAINT name DEFAULT value" (e.g. a
        // CHECK constraint, or a DEFAULT with a space in its literal) fall through to the trailing
        // catch-all below and parse as nullable instead of throwing -- silently wrong, not silently
        // absent, which this parser's own philosophy (throw on anything it can't confidently read)
        // exists to prevent.
        @"\s+(?<nullability>NOT\s+NULL|NULL)" +
        // IDENTITY appears on either side of the nullability clause across this schema
        // (Resource declares it before, PackageResource after), so both positions are optional.
        @"(\s+IDENTITY\s*\([^)]*\))?" +
        @"(\s+CONSTRAINT\s+.+)?$",
        RegexOptions.IgnoreCase);

    public static IReadOnlyList<DdlTable> ParseTables(string ddlText, Func<string, bool> tableNameFilter)
    {
        var tables = new List<DdlTable>();
        var searchStart = 0;

        while (true)
        {
            var startMatch = TableStart.Match(ddlText, searchStart);
            if (!startMatch.Success)
            {
                break;
            }

            var tableName = startMatch.Groups[1].Value;
            var openParenIndex = startMatch.Index + startMatch.Length - 1;
            var closeParenIndex = FindMatchingCloseParen(ddlText, openParenIndex);
            var body = ddlText.Substring(openParenIndex + 1, closeParenIndex - openParenIndex - 1);

            if (tableNameFilter(tableName))
            {
                tables.Add(new DdlTable("dbo", tableName, ParseColumns(body)));
            }

            searchStart = closeParenIndex + 1;
        }

        return tables;
    }

    private static int FindMatchingCloseParen(string text, int openParenIndex)
    {
        var depth = 0;
        for (var i = openParenIndex; i < text.Length; i++)
        {
            if (text[i] == '(')
            {
                depth++;
            }
            else if (text[i] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        throw new FormatException("Unbalanced parentheses in DDL table body.");
    }

    private static IReadOnlyList<DdlColumn> ParseColumns(string body)
    {
        var columns = new List<DdlColumn>();
        foreach (var rawLine in SplitTopLevel(body, ','))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            // Table-level constraints (PRIMARY KEY (...), CONSTRAINT ... CHECK (...)) are not column
            // definitions -- skip lines that open with these keywords rather than a column name.
            if (line.StartsWith("CONSTRAINT", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("PRIMARY KEY", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("UNIQUE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            columns.Add(ParseColumn(line));
        }

        return columns;
    }

    private static IEnumerable<string> SplitTopLevel(string text, char separator)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '(')
            {
                depth++;
            }
            else if (text[i] == ')')
            {
                depth--;
            }
            else if (text[i] == separator && depth == 0)
            {
                yield return text.Substring(start, i - start);
                start = i + 1;
            }
        }

        yield return text.Substring(start);
    }

    private static DdlColumn ParseColumn(string line)
    {
        var match = ColumnLine.Match(line);
        if (!match.Success)
        {
            throw new FormatException($"Could not parse DDL column line: '{line}'");
        }

        var name = match.Groups["name"].Value;
#pragma warning disable CA1308 // Lowercase is intentional: matches the existing hand-written SqlCatalog convention (e.g. "nvarchar", not "NVARCHAR").
        var sqlType = match.Groups["type"].Value.ToLowerInvariant();
#pragma warning restore CA1308

        int? maxLength = null;
        if (match.Groups["args"].Success)
        {
            var firstArg = match.Groups["args"].Value.Split(',')[0].Trim();
            if (int.TryParse(firstArg, out var parsed))
            {
                maxLength = parsed;
            }
            // else: MAX, or a non-numeric first arg -- MaxLength stays null, matching the existing
            // hand-written convention (e.g. TextOverflow's NVARCHAR(MAX) already models as MaxLength: null).
        }

        var collation = match.Groups["collation"].Success ? match.Groups["collation"].Value : null;
        // TrimStart + StartsWith rather than a literal-space Replace: the regex's \s+ between NOT and
        // NULL accepts a tab, which a single-character space Replace would miss.
        var isNullable = !match.Groups["nullability"].Value.TrimStart()
            .StartsWith("NOT", StringComparison.OrdinalIgnoreCase);

        return new DdlColumn(name, sqlType, maxLength, collation, isNullable);
    }
}
