/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath string manipulation function implementations (Phase 23, Week 4).
 * Implements indexOf(), substring(), startsWith(), endsWith(), upper(), lower(),
 * length(), replace(), matches(), replaceMatches(), toChars(), join().
 */

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Attributes;
using Ignixa.FhirPath.Expressions;

namespace Ignixa.FhirPath.Evaluation.Functions;

/// <summary>
/// String manipulation function implementations for FhirPath expressions.
/// </summary>
internal static class StringFunctions
{
    /// <summary>
    /// indexOf() - Returns the 0-based index of the first occurrence of a substring.
    /// Returns -1 if substring is not found.
    /// </summary>
    [FhirPathFunction("indexOf",
        SupportedContexts = "string-integer",
        ReturnType = "integer",
        MinArguments = 1,
        MaxArguments = 1,
        Category = "String",
        Description = "Returns the 0-based index of the first occurrence of a substring")]
    public static IEnumerable<IElement> IndexOf(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("indexOf() requires a substring argument");

        var list = focus.ToList();
        if (list.Count != 1 || list[0].Value is not string str)
            return [];

        var substringResult = evaluateExpression(focus, arguments[0], context).SingleOrDefault();
        if (substringResult?.Value is not string substring)
            return [];

        var index = str.IndexOf(substring, StringComparison.Ordinal);
        return [FunctionHelpers.CreateInteger(index)];
    }

    /// <summary>
    /// substring() - Extracts a substring starting at a 0-based index.
    /// Optionally accepts a length parameter.
    /// </summary>
    [FhirPathFunction("substring",
        SupportedContexts = "string-string",
        ReturnType = "string",
        MinArguments = 1,
        MaxArguments = 2,
        Category = "String",
        Description = "Extracts a substring starting at a 0-based index")]
    public static IEnumerable<IElement> Substring(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("substring() requires a start argument");

        var list = focus.ToList();
        if (list.Count != 1 || list[0].Value is not string str)
            return [];

        var startResult = evaluateExpression(focus, arguments[0], context).SingleOrDefault();
        if (startResult?.Value is not int start)
            return [];

        if (start < 0 || start >= str.Length)
            return [];

        int? length = null;
        if (arguments.Count > 1)
        {
            var lengthResult = evaluateExpression(focus, arguments[1], context).SingleOrDefault();
            if (lengthResult?.Value is int len)
                length = len;
        }

        var result = length.HasValue
            ? str.Substring(start, Math.Min(length.Value, str.Length - start))
            : str.Substring(start);

        return [FunctionHelpers.CreateString(result)];
    }

    /// <summary>
    /// startsWith() - Tests if a string starts with a given prefix.
    /// </summary>
    [FhirPathFunction("startsWith",
        SupportedContexts = "string-boolean",
        ReturnType = "boolean",
        MinArguments = 1,
        MaxArguments = 1,
        Category = "String",
        Description = "Tests if a string starts with a given prefix")]
    public static IEnumerable<IElement> StartsWith(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("startsWith() requires a prefix argument");

        var list = focus.ToList();
        if (list.Count != 1 || list[0].Value is not string str)
            return [];

        var prefixResult = evaluateExpression(focus, arguments[0], context).SingleOrDefault();
        if (prefixResult?.Value is not string prefix)
            return [];

        return FunctionHelpers.ReturnBoolean(str.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// endsWith() - Tests if a string ends with a given suffix.
    /// </summary>
    [FhirPathFunction("endsWith",
        SupportedContexts = "string-boolean",
        ReturnType = "boolean",
        MinArguments = 1,
        MaxArguments = 1,
        Category = "String",
        Description = "Tests if a string ends with a given suffix")]
    public static IEnumerable<IElement> EndsWith(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("endsWith() requires a suffix argument");

        var list = focus.ToList();
        if (list.Count != 1 || list[0].Value is not string str)
            return [];

        var suffixResult = evaluateExpression(focus, arguments[0], context).SingleOrDefault();
        if (suffixResult?.Value is not string suffix)
            return [];

        return FunctionHelpers.ReturnBoolean(str.EndsWith(suffix, StringComparison.Ordinal));
    }

    /// <summary>
    /// upper() - Converts a string to uppercase.
    /// </summary>
    [FhirPathFunction("upper",
        SupportedContexts = "string-string",
        ReturnType = "string",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "String",
        Description = "Converts a string to uppercase")]
    public static IEnumerable<IElement> Upper(IEnumerable<IElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1 || list[0].Value is not string str)
            return [];

        return [FunctionHelpers.CreateString(str.ToUpperInvariant())];
    }

    /// <summary>
    /// lower() - Converts a string to lowercase.
    /// </summary>
    [FhirPathFunction("lower",
        SupportedContexts = "string-string",
        ReturnType = "string",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "String",
        Description = "Converts a string to lowercase")]
    public static IEnumerable<IElement> Lower(IEnumerable<IElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1 || list[0].Value is not string str)
            return [];

        // FhirPath lower() function explicitly requires lowercase, ToLowerInvariant is intentional
#pragma warning disable CA1308 // Normalize strings to uppercase
        return [FunctionHelpers.CreateString(str.ToLowerInvariant())];
#pragma warning restore CA1308 // Normalize strings to uppercase
    }

    /// <summary>
    /// length() - Returns the number of characters in a string.
    /// </summary>
    [FhirPathFunction("length",
        SupportedContexts = "string-integer",
        ReturnType = "integer",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "String",
        Description = "Returns the number of characters in a string")]
    public static IEnumerable<IElement> Length(IEnumerable<IElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1 || list[0].Value is not string str)
            return [];

        return [FunctionHelpers.CreateInteger(str.Length)];
    }

    /// <summary>
    /// replace() - Replaces all occurrences of a pattern string with a substitution string.
    /// </summary>
    [FhirPathFunction("replace",
        SupportedContexts = "string-string",
        ReturnType = "string",
        MinArguments = 2,
        MaxArguments = 2,
        Category = "String",
        Description = "Replaces all occurrences of a pattern string with a substitution string")]
    public static IEnumerable<IElement> Replace(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        if (arguments.Count < 2)
            throw new ArgumentException("replace() requires pattern and substitution arguments");

        var list = focus.ToList();
        if (list.Count != 1 || list[0].Value is not string str)
            return [];

        var patternResult = evaluateExpression(focus, arguments[0], context).SingleOrDefault();
        var substitutionResult = evaluateExpression(focus, arguments[1], context).SingleOrDefault();

        if (patternResult?.Value is not string pattern || substitutionResult?.Value is not string substitution)
            return [];

        // Empty pattern: insert substitution between every character (per FHIRPath spec)
        // Example: 'abc'.replace('', 'x') → 'xaxbxcx'
        if (string.IsNullOrEmpty(pattern))
        {
            if (string.IsNullOrEmpty(str))
                return [FunctionHelpers.CreateString(substitution)];

            var result = new StringBuilder(substitution);
            foreach (var ch in str)
            {
                result.Append(ch);
                result.Append(substitution);
            }
            return [FunctionHelpers.CreateString(result.ToString())];
        }

        var replaced = str.Replace(pattern, substitution, StringComparison.Ordinal);
        return [FunctionHelpers.CreateString(replaced)];
    }

    /// <summary>
    /// matches() - Tests if a string matches a regular expression pattern.
    /// </summary>
    [FhirPathFunction("matches",
        SupportedContexts = "string-boolean",
        ReturnType = "boolean",
        MinArguments = 1,
        MaxArguments = 1,
        Category = "String",
        Description = "Tests if a string matches a regular expression pattern")]
    public static IEnumerable<IElement> Matches(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("matches() requires a regex argument");

        var list = focus.ToList();
        if (list.Count != 1 || list[0].Value is not string str)
            return [];

        var regexResult = evaluateExpression(focus, arguments[0], context).SingleOrDefault();
        if (regexResult?.Value is not string pattern)
            return [];

        try
        {
            var regex = new Regex(pattern);
            return FunctionHelpers.ReturnBoolean(regex.IsMatch(str));
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// replaceMatches() - Replaces all regex pattern matches with a substitution string.
    /// </summary>
    [FhirPathFunction("replaceMatches",
        SupportedContexts = "string-string",
        ReturnType = "string",
        MinArguments = 2,
        MaxArguments = 2,
        Category = "String",
        Description = "Replaces all regex pattern matches with a substitution string")]
    public static IEnumerable<IElement> ReplaceMatches(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        if (arguments.Count < 2)
            throw new ArgumentException("replaceMatches() requires pattern and substitution arguments");

        var list = focus.ToList();
        if (list.Count != 1 || list[0].Value is not string str)
            return [];

        var patternResult = evaluateExpression(focus, arguments[0], context).SingleOrDefault();
        var substitutionResult = evaluateExpression(focus, arguments[1], context).SingleOrDefault();

        if (patternResult?.Value is not string pattern || substitutionResult?.Value is not string substitution)
            return [];

        // Empty pattern: return original string unchanged (regex behavior)
        if (string.IsNullOrEmpty(pattern))
            return [FunctionHelpers.CreateString(str)];

        try
        {
            var regex = new Regex(pattern);
            var result = regex.Replace(str, substitution);
            return [FunctionHelpers.CreateString(result)];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// toChars() - Splits a string into individual characters.
    /// Returns a collection of single-character strings.
    /// </summary>
    [FhirPathFunction("toChars",
        SupportedContexts = "string-string",
        ReturnType = "string",
        SupportsCollections = true,
        MinArguments = 0,
        MaxArguments = 0,
        Category = "String",
        Description = "Splits a string into individual characters")]
    public static IEnumerable<IElement> ToChars(IEnumerable<IElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1 || list[0].Value is not string str)
            return [];

        return str.Select(c => FunctionHelpers.CreateString(c.ToString()));
    }

    /// <summary>
    /// join() - Concatenates a collection of strings with an optional separator.
    /// If separator is not provided, concatenates without separator.
    /// </summary>
    [FhirPathFunction("join",
        SupportedContexts = "string-string",
        ReturnType = "string",
        SupportsCollections = true,
        MinArguments = 0,
        MaxArguments = 1,
        Category = "String",
        Description = "Concatenates a collection of strings with an optional separator")]
    public static IEnumerable<IElement> Join(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        var focusElements = focus.ToList();

        // Get the separator (default to empty string if not provided)
        var separator = string.Empty;
        if (arguments.Count > 0)
        {
            var sepResult = evaluateExpression(focusElements, arguments[0], context).ToList();
            if (sepResult.Count > 0 && sepResult[0].Value is string sepStr)
            {
                separator = sepStr;
            }
        }

        // Concatenate all string values with the separator
        var strings = focusElements
            .Where(e => e.Value is string)
            .Select(e => (string)e.Value!)
            .ToList();

        // join() always returns a string, even if empty
        var result = string.Join(separator, strings);
        return [FunctionHelpers.CreateString(result)];
    }

    /// <summary>
    /// trim() - Removes leading and trailing whitespace from a string.
    /// </summary>
    [FhirPathFunction("trim",
        SupportedContexts = "string-string",
        ReturnType = "string",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "String",
        Description = "Removes leading and trailing whitespace")]
    public static IEnumerable<IElement> Trim(IEnumerable<IElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1 || list[0].Value is not string str)
            return [];

        return [FunctionHelpers.CreateString(str.Trim())];
    }

    /// <summary>
    /// split() - Splits a string by a delimiter into a collection of strings.
    /// </summary>
    [FhirPathFunction("split",
        SupportedContexts = "string-string",
        ReturnType = "string",
        SupportsCollections = true,
        MinArguments = 1,
        MaxArguments = 1,
        Category = "String",
        Description = "Splits a string by a delimiter")]
    public static IEnumerable<IElement> Split(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("split() requires a delimiter argument");

        var list = focus.ToList();
        if (list.Count != 1 || list[0].Value is not string str)
            return [];

        var delimiterResult = evaluateExpression(focus, arguments[0], context).SingleOrDefault();
        if (delimiterResult?.Value is not string delimiter)
            return [];

        var parts = str.Split(delimiter, StringSplitOptions.None);
        return parts.Select(p => FunctionHelpers.CreateString(p));
    }

    /// <summary>
    /// contains() - Tests if a string contains a substring (case-sensitive).
    /// </summary>
    [FhirPathFunction("contains",
        SupportedContexts = "string-boolean",
        ReturnType = "boolean",
        MinArguments = 1,
        MaxArguments = 1,
        Category = "String",
        Description = "Tests if a string contains a substring")]
    public static IEnumerable<IElement> Contains(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("contains() requires a substring argument");

        var list = focus.ToList();
        if (list.Count != 1 || list[0].Value is not string str)
            return [];

        var substringResult = evaluateExpression(focus, arguments[0], context).SingleOrDefault();
        if (substringResult?.Value is not string substring)
            return [];

        return FunctionHelpers.ReturnBoolean(str.Contains(substring, StringComparison.Ordinal));
    }

    /// <summary>
    /// encode() - Encodes the string using the specified encoding.
    /// Supports base64, urlbase64, and hex encodings.
    /// </summary>
    [FhirPathFunction("encode",
        SupportedContexts = "string-string",
        ReturnType = "string",
        MinArguments = 1,
        MaxArguments = 1,
        Category = "String",
        Description = "Encodes the string using the specified encoding")]
    public static IEnumerable<IElement> Encode(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        var element = focus.SingleOrDefault();
        if (element?.Value is not string str)
            return [];

        var encodingTypeResult = evaluateExpression(focus, arguments[0], context);
        var encodingType = encodingTypeResult.FirstOrDefault()?.Value?.ToString();

        if (encodingType is null)
            return [];

#pragma warning disable CA1308 // Normalize strings to uppercase
        return encodingType.ToLowerInvariant() switch
        {
            "urlbase64" => [FunctionHelpers.CreateString(Convert.ToBase64String(Encoding.UTF8.GetBytes(str)).Replace('+', '-').Replace('/', '_'))],
            "base64" => [FunctionHelpers.CreateString(Convert.ToBase64String(Encoding.UTF8.GetBytes(str)))],
            "hex" => [FunctionHelpers.CreateString(Convert.ToHexString(Encoding.UTF8.GetBytes(str)).ToLowerInvariant())],
            _ => []
        };
#pragma warning restore CA1308 // Normalize strings to uppercase
    }

    /// <summary>
    /// decode() - Decodes the string using the specified encoding.
    /// Supports base64, urlbase64, and hex encodings.
    /// </summary>
    [FhirPathFunction("decode",
        SupportedContexts = "string-string",
        ReturnType = "string",
        MinArguments = 1,
        MaxArguments = 1,
        Category = "String",
        Description = "Decodes the string using the specified encoding")]
    public static IEnumerable<IElement> Decode(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        var element = focus.SingleOrDefault();
        if (element?.Value is not string str)
            return [];

        var encodingTypeResult = evaluateExpression(focus, arguments[0], context);
        var encodingType = encodingTypeResult.FirstOrDefault()?.Value?.ToString();

        if (encodingType is null)
            return [];

        try
        {
#pragma warning disable CA1308 // Normalize strings to uppercase
            return encodingType.ToLowerInvariant() switch
            {
                "urlbase64" => [FunctionHelpers.CreateString(Encoding.UTF8.GetString(Convert.FromBase64String(str.Replace('-', '+').Replace('_', '/'))))],
                "base64" => [FunctionHelpers.CreateString(Encoding.UTF8.GetString(Convert.FromBase64String(str)))],
                "hex" => [FunctionHelpers.CreateString(Encoding.UTF8.GetString(Convert.FromHexString(str)))],
                _ => []
            };
#pragma warning restore CA1308 // Normalize strings to uppercase
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// escape() - Escapes the string using the specified escape type.
    /// Supports html, json, and url escape types.
    /// </summary>
    [FhirPathFunction("escape",
        SupportedContexts = "string-string",
        ReturnType = "string",
        MinArguments = 1,
        MaxArguments = 1,
        Category = "String",
        Description = "Escapes the string using the specified escape type")]
    public static IEnumerable<IElement> Escape(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        var element = focus.SingleOrDefault();
        if (element?.Value is not string str)
            return [];

        var escapeTypeResult = evaluateExpression(focus, arguments[0], context);
        var escapeType = escapeTypeResult.FirstOrDefault()?.Value?.ToString();

        if (escapeType is null)
            return [];

#pragma warning disable CA1308 // Normalize strings to uppercase
        return escapeType.ToLowerInvariant() switch
        {
            "html" => [FunctionHelpers.CreateString(WebUtility.HtmlEncode(str))],
            "json" => [FunctionHelpers.CreateString(EscapeJsonString(str))],
            "url" => [FunctionHelpers.CreateString(Uri.EscapeDataString(str))],
            _ => []
        };
#pragma warning restore CA1308 // Normalize strings to uppercase
    }

    /// <summary>
    /// Escapes a string for JSON using minimal escaping (only control characters, quotes, and backslashes).
    /// Unlike JsonSerializer, this does not escape characters like &lt; as \u003C.
    /// </summary>
    private static string EscapeJsonString(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append($"\\u{(int)c:X4}");
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// unescape() - Unescapes the string using the specified escape type.
    /// Supports html, json, and url escape types.
    /// </summary>
    [FhirPathFunction("unescape",
        SupportedContexts = "string-string",
        ReturnType = "string",
        MinArguments = 1,
        MaxArguments = 1,
        Category = "String",
        Description = "Unescapes the string using the specified escape type")]
    public static IEnumerable<IElement> Unescape(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        var element = focus.SingleOrDefault();
        if (element?.Value is not string str)
            return [];

        var escapeTypeResult = evaluateExpression(focus, arguments[0], context);
        var escapeType = escapeTypeResult.FirstOrDefault()?.Value?.ToString();

        if (escapeType is null)
            return [];

        try
        {
#pragma warning disable CA1308 // Normalize strings to uppercase
            return escapeType.ToLowerInvariant() switch
            {
                "html" => [FunctionHelpers.CreateString(WebUtility.HtmlDecode(str))],
                "json" => [FunctionHelpers.CreateString(UnescapeJsonString(str))],
                "url" => [FunctionHelpers.CreateString(Uri.UnescapeDataString(str))],
                _ => []
            };
#pragma warning restore CA1308 // Normalize strings to uppercase
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Unescapes a JSON-escaped string by converting JSON escape sequences to their corresponding characters.
    /// </summary>
    private static string UnescapeJsonString(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                var next = s[i + 1];
                switch (next)
                {
                    case '"': sb.Append('"'); i++; break;
                    case '\\': sb.Append('\\'); i++; break;
                    case '/': sb.Append('/'); i++; break;
                    case 'b': sb.Append('\b'); i++; break;
                    case 'f': sb.Append('\f'); i++; break;
                    case 'n': sb.Append('\n'); i++; break;
                    case 'r': sb.Append('\r'); i++; break;
                    case 't': sb.Append('\t'); i++; break;
                    case 'u':
                        if (i + 5 < s.Length)
                        {
                            var hex = s.Substring(i + 2, 4);
                            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var codepoint))
                            {
                                sb.Append((char)codepoint);
                                i += 5;
                                break;
                            }
                        }
                        sb.Append(s[i]); // Invalid escape, keep as-is
                        break;
                    default:
                        sb.Append(s[i]); // Unknown escape, keep backslash
                        break;
                }
            }
            else
            {
                sb.Append(s[i]);
            }
        }
        return sb.ToString();
    }
}
