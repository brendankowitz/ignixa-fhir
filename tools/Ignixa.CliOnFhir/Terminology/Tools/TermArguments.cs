// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>Checks the arguments the terminology tools share.</summary>
internal static class TermArguments
{
    public const int MaxTextLength = 512;

    /// <summary><paramref name="value"/> trimmed.</summary>
    /// <exception cref="ToolRequestException">Missing, blank or longer than <see cref="MaxTextLength"/>.</exception>
    public static string Required(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw ToolRequestException.Error("invalid-parameter", $"'{name}' is required.");
        }

        return Bounded(value, name)!;
    }

    /// <summary><paramref name="value"/> trimmed, or null when missing or blank.</summary>
    public static string? Optional(string? value, string name) => string.IsNullOrWhiteSpace(value) ? null : Bounded(value, name);

    private static string? Bounded(string value, string name)
    {
        string trimmed = value.Trim();
        return trimmed.Length <= MaxTextLength
            ? trimmed
            : throw ToolRequestException.Error("invalid-parameter", $"'{name}' is longer than {MaxTextLength} characters.");
    }
}
