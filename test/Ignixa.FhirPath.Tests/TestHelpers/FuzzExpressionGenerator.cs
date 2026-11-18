/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Seeded FHIRPath expression generators for the exception-contract fuzz tests.
 */

using System.Text;

// A seeded System.Random is the point: the same seed must produce the same expressions on every run, and
// nothing here is security-sensitive.
#pragma warning disable CA5394

namespace Ignixa.FhirPath.Tests.TestHelpers;

/// <summary>
/// Produces FHIRPath-shaped text deterministically from a seed: token soup and mutated seeds for the parser,
/// and type-plausible function and operator chains for the evaluator.
/// </summary>
internal sealed class FuzzExpressionGenerator(int seed)
{
    private const string MutationAlphabet = "()[]{}.,'\"$%@ 0123456789+-*/<>=!~&|abxyz\\u";

    private static readonly string[] Tokens =
    [
        "name", "given", "family", "telecom", "value", "birthDate", "active", "multipleBirthInteger", "id", "$this",
        "$index", "$total", "%resource", "%ucum", "1", "0", "-1", "2147483647", "2147483648", "1.5", "0.0",
        "99999999999999999999", "9223372036854775808L", "79228162514264337593543950336.0", "'a'", "''", "'abc'", "'('",
        "'[a-z'", "'\\'", "'\\uD800'", "'\\uD83D\\uDE00'", "'\\uDC00'", "'%'", "@2020", "@2020-02-30", "@T25:00",
        "true", "false", "{}", "1 'mg'", "2 days", "'kg'", ".", "(", ")", "[", "]", "{", "}", ",", "+", "-", "*",
        "/", "div", "mod", "&", "|", "=", "!=", "~", "<", ">", "and", "or", "xor", "implies", "in", "contains", "is",
        "as", "where(", "select(", "first()", "last()", "tail()", "skip(", "take(", "single()", "count()", "exists()",
        "all(", "union(", "iif(", "aggregate(", "ofType(", "children()", "toString()", "toInteger()", "toDecimal()",
        "toQuantity()", "substring(", "indexOf(", "replace(", "matches(", "startsWith(", "upper()", "length()",
        "split(", "join(", "encode(", "abs()", "round(", "sqrt()", "ln()", "log(", "power(", "exp()", "sum()",
        "min()", "max()", "avg()", "lowBoundary()", "defineVariable(", "sort(", "Patient", "string", "Quantity",
        "`a`", "\"x\"",
    ];

    private static readonly string[] SeedExpressions =
    [
        "name.where(use='official').given.first()", "telecom.where(system='phone').value.matches('[0-9-]+')",
        "birthDate < today() - 18 years", "name.select(given.first() + ' ' + family)", "(1.5 + 2) * 3 / 4 mod 2",
        "name.given.distinct().count() > 1", "iif(active, 'a', 'b')", "name.given.skip(1).take(2).join(',')",
        "'abc'.substring(1,2).upper()", "@2020-01-01 + 1 month", "1 'kg' * 2 'm'", "name.all(given.exists())",
        "name.given.aggregate($this + $total, '')", "name.children().count()",
    ];

    private static readonly string[] Operands =
    [
        "name", "name.given", "name.family", "telecom", "telecom.value", "birthDate", "active",
        "multipleBirthInteger", "id", "gender", "foo", "$this", "{}", "name.given.first()", "1", "0", "-1",
        "2147483647", "1.5", "99999999999L", "'a'", "''", "'abc'", "'('", "'[a-z'", "true", "@2020", "@T10:00",
        "1 'mg'", "2 days", "'1e5'",
    ];

    private static readonly string[] NoArgumentCalls =
    [
        "first()", "last()", "tail()", "single()", "count()", "exists()", "empty()", "distinct()", "not()",
        "allTrue()", "toString()", "toInteger()", "toDecimal()", "toDate()", "toQuantity()", "upper()", "length()",
        "toChars()", "trim()", "abs()", "ceiling()", "floor()", "truncate()", "sqrt()", "ln()", "exp()", "sum()",
        "min()", "max()", "avg()", "lowBoundary()", "highBoundary()", "precision()", "children()", "flatten()",
        "round()",
    ];

    private static readonly string[] ArgumentCalls =
    [
        "where(@)", "select(@)", "all(@)", "skip(@)", "take(@)", "substring(@)", "substring(@,@)", "indexOf(@)",
        "lastIndexOf(@)", "replace(@,@)", "matches(@)", "matchesFull(@)", "replaceMatches(@,@)", "startsWith(@)",
        "endsWith(@)", "contains(@)", "split(@)", "join(@)", "encode(@)", "decode(@)", "escape(@)", "unescape(@)",
        "round(@)", "log(@)", "power(@)", "union(@)", "combine(@)", "intersect(@)", "exclude(@)", "subsetOf(@)",
        "iif(@,@,@)", "aggregate(@,@)", "sort(@)", "slice(@,@)", "coalesce(@,@)", "defineVariable('v',@)",
        "lowBoundary(@)", "toDecimal(@)", "repeat(@)", "trace(@,@)",
    ];

    private static readonly string[] BinaryOperators =
    [
        "+", "-", "*", "/", "div", "mod", "&", "|", "=", "!=", "~", "<", ">", "<=", ">=", "and", "or", "xor",
        "implies", "in", "contains", "is", "as",
    ];

    private readonly Random _random = new(seed);

    /// <summary>Random tokens joined, some followed by a space.</summary>
    public string TokenSoup()
    {
        var builder = new StringBuilder();
        var count = _random.Next(1, 9);
        for (var i = 0; i < count; i++)
        {
            builder.Append(Tokens[_random.Next(Tokens.Length)]);
            if (_random.Next(3) == 0)
            {
                builder.Append(' ');
            }
        }

        return builder.ToString();
    }

    /// <summary>A valid expression with one or two characters changed, inserted or removed.</summary>
    public string MutatedSeed() => Mutate(SeedExpressions[_random.Next(SeedExpressions.Length)]);

    /// <summary>Token soup with up to four character-level mutations.</summary>
    public string MutatedSoup() => Mutate(Mutate(TokenSoup()));

    /// <summary>An expression chaining functions and operators, nested to <paramref name="depth"/>.</summary>
    public string Chain(int depth)
    {
        var operand = Operands[_random.Next(Operands.Length)];
        if (depth <= 0)
        {
            return operand;
        }

        return _random.Next(10) switch
        {
            < 3 => $"{operand}.{NoArgumentCalls[_random.Next(NoArgumentCalls.Length)]}",
            < 6 => $"{operand}.{FillArguments(ArgumentCalls[_random.Next(ArgumentCalls.Length)], depth)}",
            < 8 => $"{Chain(depth - 1)} {BinaryOperators[_random.Next(BinaryOperators.Length)]} {Chain(depth - 1)}",
            9 => $"{Chain(depth - 1)}.{NoArgumentCalls[_random.Next(NoArgumentCalls.Length)]}"
                + $".{NoArgumentCalls[_random.Next(NoArgumentCalls.Length)]}",
            _ => $"({Chain(depth - 1)}).{FillArguments(ArgumentCalls[_random.Next(ArgumentCalls.Length)], depth - 1)}",
        };
    }

    private string FillArguments(string call, int depth)
    {
        var builder = new StringBuilder();
        foreach (var c in call)
        {
            builder.Append(c == '@' ? Chain(_random.Next(2) == 0 ? 0 : depth - 1) : c.ToString());
        }

        return builder.ToString();
    }

    private string Mutate(string expression)
    {
        var mutations = _random.Next(1, 3);
        for (var i = 0; i < mutations && expression.Length > 0; i++)
        {
            var position = _random.Next(expression.Length);
            var replacement = MutationAlphabet[_random.Next(MutationAlphabet.Length)];
            expression = _random.Next(3) switch
            {
                0 => expression.Remove(position, 1).Insert(position, replacement.ToString()),
                1 => expression.Remove(position, 1),
                _ => expression.Insert(position, replacement.ToString()),
            };
        }

        return expression;
    }
}
