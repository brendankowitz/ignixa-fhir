/* Copyright (c) 2025, Ignixa Contributors */

using Ignixa.FhirMappingLanguage.TypeSystem;

namespace Ignixa.FhirMappingLanguage.Parser;

/// <summary>
/// Exception thrown when type validation fails.
/// </summary>
public class TypeValidationException : Exception
{
    public TypeValidationException(string message, IEnumerable<TypeValidationError> errors)
        : base(FormatMessage(message, errors))
    {
        Errors = errors?.ToList() ?? [];
    }

    public IReadOnlyList<TypeValidationError> Errors { get; }

    private static string FormatMessage(string message, IEnumerable<TypeValidationError> errors)
    {
        var errorList = errors?.ToList() ?? [];
        if (errorList.Count == 0)
        {
            return message;
        }

        var formattedErrors = string.Join(Environment.NewLine, errorList.Select(e => $"  - {e}"));
        return $"{message}:{Environment.NewLine}{formattedErrors}";
    }
}
