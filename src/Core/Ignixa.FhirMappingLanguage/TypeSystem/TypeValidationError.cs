/* Copyright (c) 2025, Ignixa Contributors */

using Ignixa.FhirMappingLanguage.Expressions;

namespace Ignixa.FhirMappingLanguage.TypeSystem;

/// <summary>
/// Represents a type validation error.
/// </summary>
public class TypeValidationError
{
    public TypeValidationError(string message, ISourcePositionInfo? location = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        Message = message;
        Location = location;
    }

    public string Message { get; }
    public ISourcePositionInfo? Location { get; }

    public override string ToString()
    {
        if (Location is not null)
        {
            return $"{Location.LineNumber}:{Location.LinePosition} - {Message}";
        }
        return Message;
    }
}
