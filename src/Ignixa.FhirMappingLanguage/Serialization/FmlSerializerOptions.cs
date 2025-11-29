/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Configuration options for FML serialization.
 */

namespace Ignixa.FhirMappingLanguage.Serialization;

/// <summary>
/// Configuration options for FML serialization.
/// </summary>
public class FmlSerializerOptions
{
    /// <summary>
    /// Default serializer options (2-space indentation, platform line endings).
    /// </summary>
    public static FmlSerializerOptions Default { get; } = new();

    /// <summary>
    /// Indentation string (default: 2 spaces).
    /// </summary>
    public string Indent { get; init; } = "  ";

    /// <summary>
    /// Line ending (default: Environment.NewLine).
    /// </summary>
    public string NewLine { get; init; } = Environment.NewLine;
}
