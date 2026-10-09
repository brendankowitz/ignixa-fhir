/*
 * Copyright (c) 2026, Ignixa Contributors
 *
 * Shared lexical formatting for FHIR Mapping Language.
 */

using System.Globalization;
using System.Text;

namespace Ignixa.FhirMappingLanguage;

internal static class FmlText
{
    private static readonly HashSet<string> ReservedWords = new(StringComparer.Ordinal)
    {
        "map", "uses", "as", "alias", "imports", "group", "extends", "default",
        "where", "check", "log", "then", "true", "false", "types", "type",
        "first", "not_first", "last", "not_last", "only_one", "share", "single",
        "source", "target", "queried", "produced", "conceptmap", "prefix", "let",
        "constant", "contains", "in", "is", "asc", "desc", "sort", "div", "mod",
        "and", "or", "xor", "implies", "year", "month", "week", "day", "hour",
        "minute", "second", "millisecond", "years", "months", "weeks", "days",
        "hours", "minutes", "seconds", "milliseconds"
    };

    public static string Unquote(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.StartsWith("\"\"\"", StringComparison.Ordinal))
        {
            return UnquoteMarkdown(text);
        }

        if (text.Length < 2 || text[0] is not ('\'' or '"' or '`') || text[^1] != text[0])
        {
            return text;
        }

        var result = new StringBuilder(text.Length - 2);
        for (int i = 1; i < text.Length - 1; i++)
        {
            char character = text[i];
            if (character == '\'' && text[0] == '\'' && i + 1 < text.Length - 1 && text[i + 1] == '\'')
            {
                result.Append('\'');
                i++;
                continue;
            }

            if (character != '\\')
            {
                result.Append(character);
                continue;
            }

            i++;
            if (i >= text.Length - 1)
            {
                throw new FormatException("An FML escape sequence must include a character after the backslash.");
            }

            if (text[i] == 'u')
            {
                if (i + 4 >= text.Length - 1 ||
                    !ushort.TryParse(text.AsSpan(i + 1, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort code))
                {
                    throw new FormatException("An FML Unicode escape must contain exactly four hexadecimal digits.");
                }

                result.Append((char)code);
                i += 4;
                continue;
            }

            result.Append(text[i] switch
            {
                '\\' => '\\',
                '\'' => '\'',
                '"' => '"',
                '`' => '`',
                '/' => '/',
                'f' => '\f',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                _ => throw new FormatException($"Invalid FML escape sequence '\\{text[i]}'.")
            });
        }

        return result.ToString();
    }

    public static string Quote(string value, char quote = '\'')
    {
        ArgumentNullException.ThrowIfNull(value);
        if (quote is not ('\'' or '"' or '`'))
        {
            throw new ArgumentOutOfRangeException(nameof(quote), quote, "FML quotes must be single quotes, double quotes, or backticks.");
        }

        var result = new StringBuilder(value.Length + 2);
        result.Append(quote);
        foreach (char character in value)
        {
            if (character == quote || character == '\\')
            {
                result.Append('\\').Append(character);
                continue;
            }

            switch (character)
            {
                case '\f':
                    result.Append("\\f");
                    break;
                case '\n':
                    result.Append("\\n");
                    break;
                case '\r':
                    result.Append("\\r");
                    break;
                case '\t':
                    result.Append("\\t");
                    break;
                default:
                    if (char.IsControl(character))
                    {
                        result.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        result.Append(character);
                    }

                    break;
            }
        }

        return result.Append(quote).ToString();
    }

    public static string Identifier(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length == 0 || !(char.IsAsciiLetter(name[0]) || name[0] == '_') || ReservedWords.Contains(name))
        {
            return Quote(name, '`');
        }

        foreach (char character in name)
        {
            if (!(char.IsAsciiLetterOrDigit(character) || character == '_'))
            {
                return Quote(name, '`');
            }
        }

        return name;
    }

    private static string UnquoteMarkdown(string text)
    {
        int end = text.Length;
        if (text.EndsWith("\r\n", StringComparison.Ordinal))
        {
            end -= 2;
        }
        else if (text[^1] is '\r' or '\n')
        {
            end--;
        }

        if (end < 6 || !text.AsSpan(end - 3, 3).SequenceEqual("\"\"\""))
        {
            throw new FormatException("An FML markdown literal must end with triple quotes.");
        }

        int start = 3;
        end -= 3;
        if (start >= end || text[start] is not ('\r' or '\n'))
        {
            throw new FormatException("An FML markdown literal must start on a new line.");
        }

        start += text[start] == '\r' && start + 1 < end && text[start + 1] == '\n' ? 2 : 1;
        if (end <= start || text[end - 1] is not ('\r' or '\n'))
        {
            throw new FormatException("An FML markdown literal must end on a new line.");
        }

        end -= end - start >= 2 && text[end - 2] == '\r' && text[end - 1] == '\n' ? 2 : 1;
        return text[start..end];
    }
}
