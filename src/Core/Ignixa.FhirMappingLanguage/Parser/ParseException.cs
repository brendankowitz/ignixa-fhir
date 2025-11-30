/* Copyright (c) 2025, Ignixa Contributors */

using Superpower.Model;

namespace Ignixa.FhirMappingLanguage.Parser;

/// <summary>
/// Exception thrown when parsing fails.
/// </summary>
public class ParseException : Exception
{
    public ParseException(string message, Position position) : base(message)
    {
        Position = position;
    }

    public ParseException(string message, Position position, Exception innerException)
        : base(message, innerException)
    {
        Position = position;
    }

    public Position Position { get; }
}
