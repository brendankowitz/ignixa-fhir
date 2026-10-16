// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.CliOnFhir.Views;

/// <summary>
/// Lexical limits on the FHIRPath of a ViewDefinition, checked before the parser sees any of it. The FHIRPath parser and evaluator
/// recurse on the shape of an expression, so a ViewDefinition from the folder (which is untrusted) with a path nested hundreds of
/// parentheses deep, or a <c>repeat</c> that never moves on (<c>$this</c>), ends the whole process with a stack overflow, which no
/// <c>catch</c> can stop. These guards refuse such a view with an error instead. They are the tool's; a depth limit in the Core
/// parser and evaluator, and a repeat that cannot be seen to stand still lexically (<c>$this.where(true)</c>), are separate follow-ups.
/// </summary>
internal static class ViewExpressionGuards
{
    /// <summary>The longest FHIRPath expression a view may have.</summary>
    public const int MaxLength = 4096;

    /// <summary>The deepest bracket nesting an expression may have: parentheses, indexers and braces together.</summary>
    public const int MaxDepth = 64;

    /// <summary>
    /// The first expression of the ViewDefinition (its <c>where</c> paths, and in every select, nested select and <c>unionAll</c>
    /// branch its column <c>path</c>s, <c>forEach</c>, <c>forEachOrNull</c> and <c>repeat</c> entries) that breaks a limit, with
    /// where it is as a JSON path of defined names, or null. A <c>repeat</c> entry that cannot move on breaks one too.
    /// </summary>
    public static (string Where, string Reason)? Violation(JsonObject viewDefinition)
    {
        ArgumentNullException.ThrowIfNull(viewDefinition);

        foreach ((JsonObject clause, string path) in Items(viewDefinition["where"], "where"))
        {
            if (Check(clause["path"], $"{path}.path", isRepeat: false) is { } violation)
            {
                return violation;
            }
        }

        foreach ((JsonObject select, string path) in Items(viewDefinition["select"], "select"))
        {
            if (Select(select, path) is { } violation)
            {
                return violation;
            }
        }

        return null;
    }

    /// <summary>Why <paramref name="expression"/> breaks a limit, or null.</summary>
    public static string? Violation(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        if (expression.Length > MaxLength)
        {
            return $"is longer than {MaxLength} characters";
        }

        return Depth(expression) > MaxDepth ? $"is nested more than {MaxDepth} levels deep" : null;
    }

    private static (string Where, string Reason)? Select(JsonObject select, string path)
    {
        foreach (string name in new[] { "forEach", "forEachOrNull" })
        {
            if (Check(select[name], $"{path}.{name}", isRepeat: false) is { } violation)
            {
                return violation;
            }
        }

        if (select["repeat"] is JsonArray repeats)
        {
            for (int i = 0; i < repeats.Count; i++)
            {
                if (Check(repeats[i], $"{path}.repeat[{i}]", isRepeat: true) is { } violation)
                {
                    return violation;
                }
            }
        }

        foreach ((JsonObject column, string columnPath) in Items(select["column"], $"{path}.column"))
        {
            if (Check(column["path"], $"{columnPath}.path", isRepeat: false) is { } violation)
            {
                return violation;
            }
        }

        foreach (string nested in new[] { "select", "unionAll" })
        {
            foreach ((JsonObject child, string childPath) in Items(select[nested], $"{path}.{nested}"))
            {
                if (Select(child, childPath) is { } violation)
                {
                    return violation;
                }
            }
        }

        return null;
    }

    private static (string Where, string Reason)? Check(JsonNode? expression, string where, bool isRepeat)
    {
        if (expression is not JsonValue value || !value.TryGetValue(out string? text))
        {
            return null;
        }

        if (Violation(text) is { } reason)
        {
            return (where, reason);
        }

        return isRepeat && !MovesOn(text) ? (where, "does not move on (a repeat of $this would recurse forever)") : null;
    }

    // A repeat follows its paths from each item to the next; one that is empty or only $this is its own next item, forever.
    private static bool MovesOn(string repeat)
    {
        string current = repeat.Trim();
        while (current.Length >= 2 && current[0] == '(' && current[^1] == ')' && ClosingParenthesis(current) == current.Length - 1)
        {
            current = current[1..^1].Trim();
        }

        return current.Length > 0 && current != "$this";
    }

    // The index of the parenthesis that closes the one at 0, outside string literals and delimited identifiers.
    private static int ClosingParenthesis(string text)
    {
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            i = SkipQuoted(text, i);
            if (text[i] == '(')
            {
                depth++;
            }
            else if (text[i] == ')' && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static int Depth(string text)
    {
        int depth = 0;
        int deepest = 0;
        for (int i = 0; i < text.Length; i++)
        {
            i = SkipQuoted(text, i);
            if (text[i] is '(' or '[' or '{')
            {
                deepest = Math.Max(deepest, ++depth);
            }
            else if (text[i] is ')' or ']' or '}')
            {
                depth = Math.Max(0, depth - 1);
            }
        }

        return deepest;
    }

    // If a string literal ('...' with \ escapes) or a delimited identifier (`...`) starts at index, the index of its closing quote;
    // otherwise index itself. An unterminated one runs to the end.
    private static int SkipQuoted(string text, int index)
    {
        char quote = text[index];
        if (quote is not ('\'' or '`'))
        {
            return index;
        }

        for (int i = index + 1; i < text.Length; i++)
        {
            if (text[i] == '\\')
            {
                i++;
            }
            else if (text[i] == quote)
            {
                return i;
            }
        }

        return text.Length - 1;
    }

    private static IEnumerable<(JsonObject Object, string Path)> Items(JsonNode? node, string path)
    {
        if (node is JsonArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is JsonObject item)
                {
                    yield return (item, $"{path}[{i}]");
                }
            }
        }
    }
}
