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
/// <c>catch</c> can stop. These guards refuse such a view with an error instead. They are the tool's cheap early errors. A repeat that stands still without being lexically <c>$this</c> is stopped by the
/// evaluator (<c>repeat went deeper than 100 levels</c>); a depth limit in the Core FHIRPath parser is a separate follow-up. Iterating
/// functions nested inside each other are limited too (<see cref="MaxIteratingDepth"/>), since their cost multiplies within one call of
/// the evaluator, which cannot be cancelled; cancellation inside the Core evaluator is a separate follow-up. De-identification rules
/// and maps are held to the same limits.
/// </summary>
internal static class ViewExpressionGuards
{
    /// <summary>The longest FHIRPath expression a view may have.</summary>
    public const int MaxLength = 4096;

    /// <summary>The deepest bracket nesting an expression may have: parentheses, indexers and braces together, wherever they appear.</summary>
    public const int MaxDepth = 64;

    /// <summary>
    /// The most iterating functions an expression may nest inside each other. Each one runs its argument once per item of its input,
    /// so their cost multiplies: seven nested <c>select(...).count()</c> over ten items took minutes, in one call of the evaluator
    /// that no timeout can interrupt. Three are as deep as any real expression goes.
    /// </summary>
    public const int MaxIteratingDepth = 3;

    // Functions that evaluate their argument once per input item.
    private static readonly HashSet<string> IteratingWithArgument = new(StringComparer.Ordinal) { "where", "select", "all", "exists", "repeat", "aggregate", "sort" };

    // Functions with no argument that multiply what follows them by the size of the tree below their input.
    private static readonly HashSet<string> Expanding = new(StringComparer.Ordinal) { "descendants", "children" };

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

        if (Depth(expression) > MaxDepth)
        {
            return $"is nested more than {MaxDepth} levels deep";
        }

        return IteratingDepth(expression) > MaxIteratingDepth
            ? $"nests iterating functions (where, select, all, exists, repeat, aggregate, sort, descendants, children) more than {MaxIteratingDepth} deep, each running over the result of the one around it"
            : null;
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

    // The index of the parenthesis that closes the one at 0, counting every one: quoted text is not skipped (see Depth).
    private static int ClosingParenthesis(string text)
    {
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
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

    // Every bracket counts, wherever it is. FHIRPath has comments (/* */ and //) in which a quote or backtick means nothing, so a
    // scan that skipped quoted text could be made to stop counting by one inside a comment; counting everything cannot be
    // bypassed. The price is that a string literal with more than MaxDepth unbalanced opening brackets is refused too.
    private static int Depth(string text)
    {
        int depth = 0;
        int deepest = 0;
        foreach (char character in text)
        {
            if (character is '(' or '[' or '{')
            {
                deepest = Math.Max(deepest, ++depth);
            }
            else if (character is ')' or ']' or '}')
            {
                depth = Math.Max(0, depth - 1);
            }
        }

        return deepest;
    }

    // How many iterating calls are nested inside each other, read lexically: a call of a function that takes its argument per item
    // (with an argument) opens an iterating level until its parenthesis closes, and descendants() or children() inside such levels
    // counts as one more. As for Depth, quoted text and comments are not skipped, so a string holding "where(" counts too.
    private static int IteratingDepth(string text)
    {
        Stack<bool> frames = new();
        int open = 0;
        int deepest = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char character = text[i];
            if (character is '(' or '[' or '{')
            {
                string name = character == '(' ? NameBefore(text, i) : string.Empty;
                bool iterating = IteratingWithArgument.Contains(name) && !EmptyArguments(text, i);
                if (iterating || Expanding.Contains(name))
                {
                    deepest = Math.Max(deepest, open + 1);
                }

                frames.Push(iterating);
                open += iterating ? 1 : 0;
            }
            else if (character is ')' or ']' or '}' && frames.TryPop(out bool closed) && closed)
            {
                open--;
            }
        }

        return deepest;
    }

    // The identifier just before the opening parenthesis at index, spaces skipped; empty when there is none.
    private static string NameBefore(string text, int index)
    {
        int end = index;
        while (end > 0 && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        int start = end;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_'))
        {
            start--;
        }

        return text[start..end];
    }

    private static bool EmptyArguments(string text, int index)
    {
        int next = index + 1;
        while (next < text.Length && char.IsWhiteSpace(text[next]))
        {
            next++;
        }

        return next < text.Length && text[next] == ')';
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
