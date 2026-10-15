/*
 * Copyright (c) 2026, Ignixa Contributors
 */

using System.Globalization;
using Ignixa.Abstractions;

namespace Ignixa.FhirMappingLanguage.Transforms;

internal static class TransformValue
{
    public static object? Scalar(object? value) => value is IElement element ? element.Value : value;

    public static string String(object? value)
    {
        return Scalar(value) switch
        {
            null => string.Empty,
            bool boolean => boolean ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            object scalar => scalar.ToString() ?? string.Empty
        };
    }
}
