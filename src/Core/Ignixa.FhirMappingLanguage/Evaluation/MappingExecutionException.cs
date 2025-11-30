/* Copyright (c) 2025, Ignixa Contributors */

namespace Ignixa.FhirMappingLanguage.Evaluation;

/// <summary>
/// Exception thrown during mapping execution when in Strict error mode.
/// </summary>
public class MappingExecutionException : Exception
{
    public MappingExecutionException(string message, string? location = null, string? code = null, Exception? innerException = null)
        : base(FormatMessage(message, location, code), innerException)
    {
        Location = location;
        Code = code;
    }

    /// <summary>
    /// Gets the location where the error occurred.
    /// </summary>
    public string? Location { get; }

    /// <summary>
    /// Gets the error code.
    /// </summary>
    public string? Code { get; }

    private static string FormatMessage(string message, string? location, string? code)
    {
        List<string> parts = [];
        if (location != null) parts.Add($"Location: {location}");
        if (code != null) parts.Add($"Code: {code}");
        parts.Add(message);
        return string.Join(" - ", parts);
    }
}
