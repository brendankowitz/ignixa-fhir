// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text;

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>Folds text for matching: accents removed, upper-cased invariantly, whitespace runs collapsed to one space.</summary>
public static class TextFolding
{
    public static string Fold(string text) => Fold(text, separators: false);

    /// <summary>
    /// <see cref="Fold(string)"/> for comparing displays, which also reads <c>-</c> and <c>_</c> as spaces ("Extended-Release" is
    /// "Extended Release"); other punctuation still counts.
    /// </summary>
    public static string FoldDisplay(string text) => Fold(text, separators: true);

    private static string Fold(string text, bool separators)
    {
        ArgumentNullException.ThrowIfNull(text);

        string decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        bool space = false;
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(c) || (separators && c is '-' or '_'))
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            builder.Append(char.ToUpperInvariant(c));
        }

        return builder.ToString();
    }
}
