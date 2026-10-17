// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.FhirPath.Parser;
using Superpower;
using Superpower.Model;

namespace Ignixa.CliOnFhir.Views;

/// <summary>
/// Lexical limits on the FHIRPath of a ViewDefinition, checked before the parser sees any of it. The FHIRPath parser and evaluator
/// recurse on the shape of an expression, so a ViewDefinition from the folder (which is untrusted) with a path nested hundreds of
/// parentheses deep, or a <c>repeat</c> that never moves on (<c>$this</c>), ends the whole process with a stack overflow, which no
/// <c>catch</c> can stop. These guards refuse such a view with an error instead. They are the tool's cheap early errors. A repeat that stands still without being lexically <c>$this</c> is stopped by the
/// evaluator (<c>repeat went deeper than 100 levels</c>); a depth limit in the Core FHIRPath parser is a separate follow-up. Iterating
/// functions nested inside each other are limited too (<see cref="MaxIteratingDepth"/>), and so is their number in all
/// (<see cref="MaxIteratingCalls"/>), since their cost multiplies within one call of the evaluator, which cannot be cancelled;
/// cancellation inside the Core evaluator is a separate follow-up. De-identification rules and maps are held to the same limits.
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

    /// <summary>
    /// The most iterating calls with an argument an expression may make in all, nested or not. Chained one after another they
    /// multiply too: each <c>select</c> of ten items runs over the ten-fold result of the one before it. Six cover every expression
    /// of the SQL-on-FHIR test suite and the FML test cases.
    /// </summary>
    public const int MaxIteratingCalls = 6;

    /// <summary>
    /// The functions that evaluate their argument once per item of their input, by the lowercased name the evaluator dispatches
    /// on. <c>trace</c> runs its projection per item when a trace handler is set. Every other function the FHIRPath library marks as
    /// taking expression arguments evaluates them once (<see cref="EvaluatedOnce"/>).
    /// </summary>
    internal static IReadOnlySet<string> IteratingWithArgument { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "where", "select", "all", "exists", "repeat", "repeatall", "aggregate", "sort", "trace" };

    /// <summary>The functions taking expression arguments that evaluate each once, whatever their input (iif, coalesce, ...).</summary>
    internal static IReadOnlySet<string> EvaluatedOnce { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "iif", "coalesce", "definevariable", "difference", "duration", "comparable", "highboundary", "lowboundary" };

    // Functions with no argument that multiply what follows them by the size of the tree below their input.
    private static readonly HashSet<string> Expanding = new(StringComparer.Ordinal) { "descendants", "children" };

    // The tokenizer FhirPathParser uses (without trivia, as every consumer of these expressions constructs it), so a name is read as
    // the parser reads it: comments and whitespace before its parenthesis dropped, a `delimited` or "delimited" name unwrapped.
    private static readonly Tokenizer<FhirPathTokenKind> FhirPathTokens = FhirPathTokenizer.Create();

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

        (int depth, int calls) = Iterating(expression);
        if (depth > MaxIteratingDepth)
        {
            return $"nests iterating functions ({IteratingNames}, descendants, children) more than {MaxIteratingDepth} deep, each running over the result of the one around it";
        }

        return calls > MaxIteratingCalls
            ? $"calls iterating functions ({IteratingNames}) more than {MaxIteratingCalls} times, each running over the result of the one before it or around it"
            : null;
    }

    private static string IteratingNames => "where, select, all, exists, repeat, repeatAll, aggregate, sort, trace";

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

    // How deep iterating calls are nested inside each other, and how many there are, read from the parser's tokens: a call of a
    // function that takes its argument per item (with an argument) opens an iterating level until its parenthesis closes, and
    // descendants() or children() inside such levels counts as one more level. Text the tokenizer refuses never reaches the parser's
    // recursion (the parser refuses it first), so it has neither.
    private static (int Depth, int Calls) Iterating(string text)
    {
        Result<TokenList<FhirPathTokenKind>> tokenized = FhirPathTokens.TryTokenize(text);
        if (!tokenized.HasValue)
        {
            return (0, 0);
        }

        Token<FhirPathTokenKind>[] tokens = [.. tokenized.Value];
        Stack<bool> frames = new();
        int open = 0;
        int deepest = 0;
        int calls = 0;
        for (int i = 0; i < tokens.Length; i++)
        {
            FhirPathTokenKind kind = tokens[i].Kind;
            if (kind is FhirPathTokenKind.LeftParen or FhirPathTokenKind.LeftBracket or FhirPathTokenKind.LeftBrace)
            {
                string name = kind == FhirPathTokenKind.LeftParen && i > 0 ? FunctionName(tokens[i - 1]) : string.Empty;
                bool iterating = IteratingWithArgument.Contains(name) && !(i + 1 < tokens.Length && tokens[i + 1].Kind == FhirPathTokenKind.RightParen);
                if (iterating || Expanding.Contains(name))
                {
                    deepest = Math.Max(deepest, open + 1);
                }

                frames.Push(iterating);
                open += iterating ? 1 : 0;
                calls += iterating ? 1 : 0;
            }
            else if (kind is FhirPathTokenKind.RightParen or FhirPathTokenKind.RightBracket or FhirPathTokenKind.RightBrace && frames.TryPop(out bool closed) && closed)
            {
                open--;
            }
        }

        return (deepest, calls);
    }

    // The name a function call before a parenthesis dispatches on: the identifier unwrapped as FhirPathGrammar does, lowercased as
    // the evaluator's dispatch does; empty when the token before is not a name.
    private static string FunctionName(Token<FhirPathTokenKind> token)
    {
        if (token.Kind is not (FhirPathTokenKind.Identifier or FhirPathTokenKind.DelimitedIdentifier))
        {
            return string.Empty;
        }

        string name = token.ToStringValue();
        bool delimited = name.Length >= 2 && ((name[0] == '`' && name[^1] == '`') || (name[0] == '"' && name[^1] == '"'));
#pragma warning disable CA1308 // The evaluator's generated dispatch switches on ToLowerInvariant(); reading the name any other way could differ.
        return (delimited ? name[1..^1] : name).ToLowerInvariant();
#pragma warning restore CA1308
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
